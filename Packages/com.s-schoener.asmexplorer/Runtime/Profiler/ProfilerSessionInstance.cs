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
        static string s_SessionFileName;
        const string k_RecordingName = "__tmp.kernel.etl";
        const string k_ProcessedName = "__tmp_processed.kernel.etl";
        static string RecordingEtlFile => Path.Combine(s_SessionDir, k_RecordingName);
        static string ProcessedEtlFile => Path.Combine(s_SessionDir, k_ProcessedName);

        public static void SetupSession(string path)
        {
            StopSession();
            var p = Path.GetFullPath(path);
            s_SessionFileName = Path.GetFileName(path);
            s_SessionDir = Path.GetDirectoryName(p);
            s_Session = new TraceEventSession(KernelTraceEventParser.KernelSessionName, Path.Combine(path, RecordingEtlFile));
            s_Session.CpuSampleIntervalMSec = 1 / 4f;
            s_Session.EnableKernelProvider(KernelTraceEventParser.Keywords.Profile | KernelTraceEventParser.Keywords.ImageLoad);
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

        public static void StopSession()
        {
            if (s_Session == null)
                return;
            s_Session.Stop();
            s_Session.Dispose();
            while (s_Session.IsActive)
                Thread.Sleep(10);
            FinishSession();
            s_Session = null;
        }

        static unsafe void FinishSession()
        {
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

            // read the cleaned-up data and re-serialize it in a more helpful format
            Dictionary<MethodBase, int> methodIndex = new Dictionary<MethodBase, int>();
            var monoJitData = new List<MonoJitInfo>();
            int nextMethodIndex = -1;
            using (var stream = File.OpenWrite(s_SessionFileName))
            using (var writer = new BinaryWriter(stream, Encoding.UTF8))
            using (var reader = TraceLog.OpenOrConvert(RecordingEtlFile))
            {
                nextMethodIndex = reader.CodeAddresses.Methods.Count;
                var rawWriter = new RawWriter(writer);
                var header = new ProfilerDataHeader
                {
                    Version = 1,
                };
                long headerPos = writer.BaseStream.Position;
                rawWriter.WriteBytes(&header, sizeof(ProfilerDataHeader));

                {
                    SampleData sampleData = default;
                    uint sampleCounter = 0;
                    foreach (var evt in reader.Events)
                    {
                        var sample = evt as SampledProfileTraceData;
                        if (sample == null) continue;
                        sampleData.Address = (long)sample.InstructionPointer;
                        sampleData.ThreadId = sample.ThreadID;
                        sampleData.TimeStamp = sample.TimeStampRelativeMSec;
                        sampleData.StackTrace = (int)sample.CallStackIndex();
                        sampleData.Function = GetFunctionIndex(sample.IntructionPointerCodeAddress());
                        rawWriter.WriteBytes(&sampleData, sizeof(SampleData));
                        sampleCounter++;
                    }

                    header.NumSamples = sampleCounter;
                }

                {
                    FunctionData funcData = default;
                    foreach (var method in reader.CodeAddresses.Methods)
                    {
                        funcData.BaseAddress = method.MethodRva;
                        funcData.Length = -1;
                        funcData.Module = (int)method.MethodModuleFileIndex;
                        CopyString(funcData.Name, method.FullMethodName, FunctionData.MaxNameLength);
                        rawWriter.WriteBytes(&funcData, sizeof(FunctionData));
                    }

                    foreach (var jitData in monoJitData)
                    {
                        funcData.BaseAddress = jitData.CodeStart.ToInt64();
                        funcData.Length = jitData.CodeSize;

                        // TODO: figure out what module data to write out
                        funcData.Module = -1;

                        var fullName = MonoFunctionName(jitData.Method);
                        CopyString(funcData.Name, fullName, FunctionData.MaxNameLength);

                        rawWriter.WriteBytes(&funcData, sizeof(FunctionData));
                    }

                    header.NumFunctions = (uint)(reader.CodeAddresses.Methods.Count + monoJitData.Count);
                }

                {
                    ModuleData moduleData = default;
                    foreach (var module in reader.CodeAddresses.ModuleFiles)
                    {
                        moduleData.Checksum = module.ImageChecksum;
                        moduleData.PdbAge = module.PdbAge;
                        CopyString(moduleData.FilePath, module.FilePath, ModuleData.MaxPathLength);
                        CopyString(moduleData.PdbName, module.PdbName, ModuleData.MaxPathLength);
                        var guidBytes = module.PdbSignature.ToByteArray();
                        fixed (byte* ptr = guidBytes)
                            UnsafeUtility.MemCpy(moduleData.PdbGuid, ptr, 16);

                        rawWriter.WriteBytes(&moduleData, sizeof(ModuleData));
                    }

                    header.NumModules = (uint)reader.CodeAddresses.ModuleFiles.Count;
                }

                {
                    StackFrameData stackTraceData = default;
                    foreach (var stack in reader.CallStacks)
                    {
                        stackTraceData.Address = (long)stack.CodeAddress.Address;
                        stackTraceData.Depth = stack.Depth;
                        stackTraceData.CallerStackFrame = stack.Caller != null ? (int)stack.Caller.CallStackIndex : -1;
                        stackTraceData.Function = GetFunctionIndex(stack.CodeAddress);
                        rawWriter.WriteBytes(&stackTraceData, sizeof(StackFrameData));
                    }

                    header.NumStackTraces = (uint)reader.CallStacks.Count;
                }

                writer.BaseStream.Position = headerPos;
                rawWriter.WriteBytes(&header, sizeof(ProfilerDataHeader));
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

            int GetFunctionIndex(TraceCodeAddress address)
            {
                var method = address.Method;
                int index;
                if (method == null)
                    LookupMethod(address.Address, out index);
                else
                    index = (int)method.MethodIndex;
                return index;
            }

            MonoJitInfo LookupMethod(ulong address, out int index)
            {
                var jit = Mono.GetJitInfo(new IntPtr((long)address));
                if (jit.Method == null)
                    index = -1;
                else if (!methodIndex.TryGetValue(jit.Method, out index))
                {
                    monoJitData.Add(jit);
                    methodIndex.Add(jit.Method, nextMethodIndex);
                    index = nextMethodIndex;
                    nextMethodIndex++;
                }

                return jit;
            }

            string MonoFunctionName(MethodBase method)
            {
                if (method == null)
                    return "???";
                return method.DeclaringType.Namespace + '.' + method.DeclaringType.Name + '.' + method.Name;
            }

            void CopyString(char* dst, string s, int maxLength)
            {
                int l = s.Length < maxLength ? s.Length : maxLength;
                fixed (char* c = s)
                    UnsafeUtility.MemCpy(dst, c, sizeof(char) * l);
            }
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
