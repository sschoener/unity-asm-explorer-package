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
using UnityEditor;
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
        static string RecordingEtlFile => Path.Combine(s_SessionDir, k_RecordingName);
        static string OutputFile => Path.Combine(s_SessionDir, s_SessionOutputFileName);

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
                s_Session.EnableKernelProvider(KernelTraceEventParser.Keywords.Default);
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

        public static void StopSession(bool finish = true)
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

        static void FinishSession()
        {
            Debug.Log("Finishing profiler session, outputting to " + OutputFile);
            EditorUtility.DisplayProgressBar("Finishing profiler session", "outputting to " + OutputFile, 0);
            using (var output = File.OpenWrite(OutputFile))
                ProfilerDataSerialization.TranslateEtlFile(RecordingEtlFile, output);
            EditorUtility.ClearProgressBar();
        }
#if UNITY_EDITOR
        static void BeforeAssemblyReload()
        {
            StopSession();
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= BeforeAssemblyReload;
        }
#endif
    }
}
