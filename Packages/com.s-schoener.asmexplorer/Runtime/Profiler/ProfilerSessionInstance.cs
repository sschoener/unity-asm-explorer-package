using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace AsmExplorer.Profiler
{
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoad]
#endif
    static class ProfilerSessionInstance
    {
        static TraceEventSession s_Session;
        static string s_SessionDir;
        static string s_SessionOutputFileName;
        const string k_RecordingName = "__tmp_raw.kernel.etl";
        const string k_ProcessedName = "__tmp_processed.kernel.etl";
        static string RecordingEtlFile => Path.Combine(s_SessionDir, k_RecordingName);
        static string ProcessedEtlFile => Path.Combine(s_SessionDir, k_ProcessedName);

        public static void SetupSession(string path)
        {
            StopSession();
            var p = Path.GetFullPath(path);
            s_SessionOutputFileName = Path.GetFileName(path);
            s_SessionDir = Path.GetDirectoryName(p);
            Debug.Log("Starting profiler session, recording to " + RecordingEtlFile);
            try
            {
                s_Session = new TraceEventSession(KernelTraceEventParser.KernelSessionName, Path.Combine(path, RecordingEtlFile));
                s_Session.EnableKernelProvider(KernelTraceEventParser.Keywords.Profile | KernelTraceEventParser.Keywords.ImageLoad);
            }
            catch
            {
                StopSession(false);
                throw;
            }
        }

#if UNITY_EDITOR
        static ProfilerSessionInstance()
        {
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += BeforeAssemblyReload;
        }
#else
        [RuntimeInitializeOnLoadMethod]
        static void InstallStopHandler()
        {
            Application.quitting += StopSession;
        }
#endif

        public static void StopSession(bool finish=true)
        {
            if (s_Session == null)
                return;
            s_Session.Stop();
            s_Session.Dispose();
            while (s_Session.IsActive)
                Thread.Sleep(10);
            s_Session = null;
            Debug.Log("Stopping profiler session");
            if (finish)
                FinishSession();
        }

        static unsafe void FinishSession()
        {
            Debug.Log("Finishing profiler session, " + s_SessionOutputFileName);
            // clean up the recorded data and drop anything we're not interested in.
            using (var relogger = new ETWReloggerTraceEventSource(RecordingEtlFile, ProcessedEtlFile))
            {
                int unityProcessId = -1;
                relogger.Kernel.PerfInfoSample += data =>
                {
                    if (!data.NonProcess && IsUnityProcess(data, ref unityProcessId))
                        relogger.WriteEvent(data);
                };
                relogger.Kernel.ImageLoad += img => relogger.WriteEvent(img);
                relogger.Kernel.ImageLoadGroup += img => relogger.WriteEvent(img);
                relogger.Kernel.ImageUnload += img => relogger.WriteEvent(img);
                relogger.Kernel.ImageUnloadGroup += img => relogger.WriteEvent(img);
                relogger.Process();
            }

            bool IsUnityProcess(SampledProfileTraceData data, ref int unityProcessId)
            {
                if (unityProcessId != -1) return data.ProcessID == unityProcessId;
                if (data.ProcessID != -1 && data.ProcessName.Contains("Unity"))
                {
                    unityProcessId = data.ProcessID;
                    return true;
                }

                return false;
            }

            // read the cleaned-up data and re-serialize it in a more helpful format
            ProfilerDataSerialization.RewriteETLFile(ProcessedEtlFile, s_SessionOutputFileName);
        }

        static void BeforeAssemblyReload()
        {
            StopSession();
#if UNITY_EDITOR
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= BeforeAssemblyReload;
#endif
        }
    }
}
