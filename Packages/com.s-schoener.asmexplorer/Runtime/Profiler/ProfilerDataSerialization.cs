using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace AsmExplorer.Profiler
{
    static class ProfilerDataSerialization
    {
        public static unsafe void ReadProfilerTrace(ref ProfilerTrace trace, Stream stream, Allocator allocator)
        {
            var basePos = stream.Position;
            var reader = new RawReader(stream);
            ProfilerDataSerializationHeader serializationHeader = default;
            reader.Read(&serializationHeader);
            Debug.Assert(basePos + serializationHeader.TotalLength <= stream.Length);

            ProfilerTraceHeader traceHeader = default;
            reader.Read(&traceHeader);
            trace.Header = traceHeader;

            Read(out trace.Samples, serializationHeader.NumSamples, basePos + serializationHeader.SamplesOffset);
            Read(out trace.StackFrames, serializationHeader.NumStackFrames, basePos + serializationHeader.StackFramesOffset);
            Read(out trace.Functions, serializationHeader.NumFunctions, basePos + serializationHeader.FunctionsOffset);
            Read(out trace.Modules, serializationHeader.NumModules, basePos + serializationHeader.ModulesOffset);
            Read(out trace.Threads, serializationHeader.NumThreads, basePos + serializationHeader.ThreadsOffset);
            Read(out trace.BlobData, serializationHeader.BlobLength, basePos + serializationHeader.BlobOffset);

            void Read<T>(out NativeArray<T> arr, int num, long offset) where T : unmanaged
            {
                stream.Position = basePos + offset;
                arr = new NativeArray<T>(num, allocator, NativeArrayOptions.UninitializedMemory);
                reader.ReadBytes(arr.GetUnsafePtr(), sizeof(T) * num);
            }
        }

        public const int ProfilerTraceVersion = 2;

        public static unsafe void WriteProfilerTrace(ref ProfilerTrace trace, Stream stream)
        {
            var writer = new RawWriter(stream);
            var header = new ProfilerDataSerializationHeader
            {
                Version = ProfilerTraceVersion,
                NumFunctions = trace.Functions.Length,
                NumModules = trace.Modules.Length,
                NumSamples = trace.Samples.Length,
                NumStackFrames = trace.StackFrames.Length,
                NumThreads = trace.Threads.Length,
                BlobLength = trace.BlobData.Length
            };
            long headerPos = stream.Position;

            // we'll write the header again later
            writer.Write(&header);

            var profTrace = trace;
            writer.Write(&profTrace.Header);

            header.SamplesOffset = stream.Position - headerPos;
            writer.WriteArray(profTrace.Samples);

            header.StackFramesOffset = stream.Position - headerPos;
            writer.WriteArray(profTrace.StackFrames);

            header.FunctionsOffset = stream.Position - headerPos;
            writer.WriteArray(profTrace.Functions);

            header.ModulesOffset = stream.Position - headerPos;
            writer.WriteArray(profTrace.Modules);

            header.ThreadsOffset = stream.Position - headerPos;
            writer.WriteArray(profTrace.Threads);

            header.BlobOffset = stream.Position - headerPos;
            writer.WriteArray(profTrace.BlobData);

            stream.Flush();
            header.TotalLength = (int)(stream.Position - headerPos);
            stream.Position = headerPos;
            writer.Write(&header);

            stream.Flush();
        }

        static ProcessIndex FindUnityProcessIndex(TraceProcesses processes)
        {
            foreach (var process in processes)
            {
                if (process.Name == "Unity" && !process.CommandLine.Contains("worker"))
                    return process.ProcessIndex;
            }

            return ProcessIndex.Invalid;
        }

        struct DiscoveredData<T>
        {
            public T Invalid;
            public List<T> Data;
            public Dictionary<T, int> Indices;

            public int Count => Data.Count;

            public int AddData(T val)
            {
                if (!Indices.TryGetValue(val, out int idx))
                {
                    if (Invalid.Equals(val))
                        return -1;
                    idx = Indices[val] = Data.Count;
                    Data.Add(val);
                }

                return idx;
            }

            public static DiscoveredData<T> Make(T invalid, IEqualityComparer<T> eq=null)
            {
                return new DiscoveredData<T>()
                {
                    Invalid = invalid,
                    Data = new List<T>(),
                    Indices = eq == null ? new Dictionary<T, int>() : new Dictionary<T, int>(eq)
                };
            }
        }

        struct DiscoveredFunction
        {
            public MonoJitInfo MonoMethod;
            public MethodIndex Index;

            public static DiscoveredFunction FromMethod(MonoJitInfo method) => new DiscoveredFunction
            {
                MonoMethod = method,
                Index = MethodIndex.Invalid,
            };

            public static readonly DiscoveredFunction Invalid = new DiscoveredFunction
            {
                MonoMethod = null,
                Index = MethodIndex.Invalid
            };
        }

        struct DiscoveredModule
        {
            public ModuleFileIndex Index;
            public Module MonoModule;

            public static DiscoveredModule FromIndex(ModuleFileIndex idx) => new DiscoveredModule
            {
                Index = idx,
            };

            public static DiscoveredModule FromMonoModule(Module asm) => new DiscoveredModule
            {
                MonoModule = asm,
                Index = ModuleFileIndex.Invalid
            };

            public static readonly DiscoveredModule Invalid = new DiscoveredModule
            {
                MonoModule = null,
                Index = ModuleFileIndex.Invalid
            };
        }

        static unsafe void ReadEtlFile(string etlPath, IEnumerable<string> pdbWhitelist, out ProfilerTrace profTrace, Allocator allocator)
        {
            var monoFunctions = new Dictionary<IntPtr, MonoJitInfo>();
            var discoveredModules = DiscoveredData<DiscoveredModule>.Make(DiscoveredModule.Invalid);
            var discoveredStackFrames = DiscoveredData<CallStackIndex>.Make(CallStackIndex.Invalid);
            var discoveredFunctions = DiscoveredData<DiscoveredFunction>.Make(DiscoveredFunction.Invalid);
            var discoveredThreads = DiscoveredData<ThreadIndex>.Make(ThreadIndex.Invalid);

            string additionalSymbolPath = BurstPath;
#if UNITY_EDITOR
            additionalSymbolPath += ";" + Path.GetDirectoryName(EditorApplication.applicationPath);
#endif

            var options = new TraceLogOptions()
            {
                AlwaysResolveSymbols = true,
                LocalSymbolsOnly = false,
                AllowUnsafeSymbols = true,
                AdditionalSymbolPath = additionalSymbolPath,
                ShouldResolveSymbols = path =>
                {
                    path = path.ToLowerInvariant();
                    return pdbWhitelist.Any(x => path.EndsWith(x));
                }
            };

            profTrace = new ProfilerTrace();
            using (var trace = TraceLog.OpenOrConvert(etlPath, options))
            {
                var processIndex = FindUnityProcessIndex(trace.Processes);
                var processId = trace.Processes[processIndex].ProcessID;

                profTrace.Header = new ProfilerTraceHeader
                {
                    SamplingInterval = trace.SampleProfileInterval.TotalMilliseconds,
                    SessionStart = trace.SessionStartTime.Ticks,
                    SessionEnd = trace.SessionEndTime.Ticks,
                };

                var sampleRange = new Dictionary<MethodIndex, ulong>();
                using (var samples = new NativeList<SampleData>(Allocator.Temp))
                {
                    SampleData sampleData = default;
                    foreach (var evt in trace.Events)
                    {
                        var sample = evt as SampledProfileTraceData;
                        if (sample == null || sample.ProcessID != processId || sample.NonProcess)
                            continue;
                        // TODO sample has a count field that we are currently ignoring. should we?
                        sampleData.Address = (long)sample.InstructionPointer;
                        sampleData.ThreadIdx = discoveredThreads.AddData(sample.Thread()?.ThreadIndex ?? ThreadIndex.Invalid);
                        sampleData.TimeStamp = sample.TimeStampRelativeMSec;
                        sampleData.StackTrace = discoveredStackFrames.AddData(sample.CallStackIndex());
                        var codeAddress = sample.IntructionPointerCodeAddress();
                        sampleData.Function = GetFunctionIndex(codeAddress);
                        samples.Add(sampleData);

                        if (sampleData.Function >= 0)
                        {
                            var index = discoveredFunctions.Data[sampleData.Function].Index;
                            if (index == MethodIndex.Invalid) continue;
                            if (!sampleRange.TryGetValue(index, out ulong previousMax))
                                sampleRange[index] = sample.InstructionPointer;
                            else
                                sampleRange[index] = sample.InstructionPointer > previousMax
                                    ? sample.InstructionPointer
                                    : previousMax;
                        }
                    }
                    profTrace.Samples = samples.ToArray(allocator);
                }

                using (var stackFrames = new NativeList<StackFrameData>(Allocator.Temp))
                {
                    StackFrameData stackTraceData = default;
                    // N.B. this loop adds more stack frames as it executes
                    for (int idx = 0; idx < discoveredStackFrames.Count; idx++)
                    {
                        var stack = trace.CallStacks[discoveredStackFrames.Data[idx]];
                        stackTraceData.Address = (long)stack.CodeAddress.Address;
                        stackTraceData.Depth = stack.Depth;
                        stackTraceData.CallerStackFrame = discoveredStackFrames.AddData(stack.Caller?.CallStackIndex ?? CallStackIndex.Invalid);
                        stackTraceData.Function = GetFunctionIndex(stack.CodeAddress);
                        stackFrames.Add(stackTraceData);
                    }
                    profTrace.StackFrames = stackFrames.ToArray(allocator);
                }

                using (var functions = new NativeList<FunctionData>(Allocator.Temp))
                {
                    var burstModules = new Dictionary<int, bool>
                    {
                        {-1, false}
                    };
                    var blobData = new NativeList<byte>(Allocator.Temp);
                    FunctionData funcData = default;
                    foreach (var func in discoveredFunctions.Data)
                    {
                        bool copyCode;
                        if (func.Index != MethodIndex.Invalid)
                        {
                            var method = trace.CodeAddresses.Methods[func.Index];
                            if (method.MethodRva > 0 && method.MethodModuleFile != null) {
                                funcData.BaseAddress = method.MethodModuleFile.ImageBase + (ulong)method.MethodRva;
                            } else
                                funcData.BaseAddress = 0;
                            if (funcData.BaseAddress > 0 && method.MethodIndex != MethodIndex.Invalid && sampleRange.TryGetValue(method.MethodIndex, out var maxAddress))
                            {
                                Debug.Assert(maxAddress >= funcData.BaseAddress);
                                // use the value of the furthest sample to estimate the length of the function
                                funcData.Length = 16 + (int) (maxAddress - funcData.BaseAddress);
                            }
                            else
                                funcData.Length = 0;
                            funcData.Module = discoveredModules.AddData(DiscoveredModule.FromIndex(method.MethodModuleFileIndex));
                            funcData.Name.CopyFrom(method.FullMethodName);
                            if (!burstModules.TryGetValue((int) method.MethodModuleFileIndex, out var isBurst))
                            {
                                isBurst = method.MethodModuleFile.FilePath.ToLowerInvariant().Contains("burst");
                                burstModules[(int) method.MethodModuleFileIndex] = isBurst;
                            }
                            copyCode = isBurst && funcData.Length > 0;
                        }
                        else
                        {
                            var jitData = func.MonoMethod;
                            funcData.BaseAddress = (ulong)jitData.CodeStart.ToInt64();
                            funcData.Length = jitData.CodeSize;
                            funcData.Module = discoveredModules.AddData(DiscoveredModule.FromMonoModule(jitData.Method.Module));

                            var fullName = MonoFunctionName(jitData.Method);
                            funcData.Name.CopyFrom(fullName);
                            copyCode = true;
                        }

                        if (copyCode)
                        {
                            funcData.CodeBlobOffset = blobData.Length;
                            blobData.AddRange((void*) funcData.BaseAddress, funcData.Length);
                        }
                        else
                            funcData.CodeBlobOffset = -1;

                        functions.Add(funcData);
                    }
                    profTrace.Functions = functions.ToArray(allocator);
                    profTrace.BlobData = blobData.ToArray(allocator);
                }

                using (var modules = new NativeList<ModuleData>(Allocator.Temp))
                {
                    // make sure that all modules of the current process are included.
                    foreach (var module in trace.Processes[processIndex].LoadedModules)
                        discoveredModules.AddData(new DiscoveredModule { Index = module.ModuleFile.ModuleFileIndex });

                    ModuleData moduleData = default;
                    foreach (var dm in discoveredModules.Data)
                    {
                        if (dm.MonoModule != null)
                        {
                            moduleData = default;
                            moduleData.IsMono = true;
                            moduleData.FilePath = dm.MonoModule.Assembly.Location;
                        }
                        else
                        {
                            var module = trace.ModuleFiles[dm.Index];
                            moduleData.IsMono = false;
                            moduleData.Checksum = module.ImageChecksum;
                            moduleData.ImageBase = module.ImageBase;
                            moduleData.ImageEnd = module.ImageEnd;
                            moduleData.PdbAge = module.PdbAge;
                            moduleData.FilePath.CopyFrom(module.FilePath);
                            moduleData.PdbName.CopyFrom(module.PdbName);
                            var guidBytes = module.PdbSignature.ToByteArray();
                            fixed (byte* ptr = guidBytes)
                                UnsafeUtility.MemCpy(moduleData.PdbGuid, ptr, 16);
                        }
                        modules.Add(moduleData);
                    }
                    profTrace.Modules = modules.ToArray(allocator);
                }

                using (var threads = new NativeList<ThreadData>(Allocator.Temp))
                {
                    ThreadData threadData = default;
                    foreach (var t in discoveredThreads.Data)
                    {
                        var thread = trace.Threads[t];
                        threadData.ThreadName.CopyFrom(thread.ThreadInfo ?? "");
                        threads.Add(threadData);
                    }
                    profTrace.Threads = threads.ToArray(allocator);
                }
            }

            int GetFunctionIndex(TraceCodeAddress address)
            {
                var method = address.Method;
                if (method == null)
                {
                    var jit = Mono.GetJitInfoAnyDomain(new IntPtr((long)address.Address), out _);
                    if (jit.Method == null)
                        return -1;
                    if (!monoFunctions.TryGetValue(jit.CodeStart, out var actualJit)) {
                        monoFunctions.Add(jit.CodeStart, jit);
                        actualJit = jit;
                    }
                    return discoveredFunctions.AddData(DiscoveredFunction.FromMethod(actualJit));
                }
                return discoveredFunctions.AddData(new DiscoveredFunction
                {
                    Index = method.MethodIndex
                });
            }

            string MonoFunctionName(MethodBase method)
            {
                if (method == null)
                    return "???";
                if (method.DeclaringType.DeclaringType == null)
                {
                    return method.DeclaringType.Namespace + '.' + method.DeclaringType.Name + '.' + method.Name;
                }
                var outerMost = method.DeclaringType.DeclaringType;
                var outer = method.DeclaringType;
                return outerMost.Namespace + '.' + outerMost.Name + '+' + outer.Name + '.' + method.Name;
            }
        }

        private static readonly string[] pdbWhiteList =
        {
            "user32.dll",
            "kernelbase.dll",
            "wow64cpu.dll",
            "ntdll.dll",
            "unity.exe",
            "mono-2.0-bdwgc.dll",
            "d3d11.dll",
            "msvcrt.dll",
            "wow64.dll",
            "win32u.dll",
            "dxgi.dll",
            "win32kfull.sys",
            "kernel32.dll",
            "ntoskrnl.exe",
            "lib_burst_generated.dll"
        };

#if UNITY_EDITOR
        private const string RelativeBurstPath = "../Library/BurstCache/JIT";
#else
        private const string RelativeBurstPath = "Plugins";
#endif

        static string BurstPath => Path.GetFullPath(Path.Combine(Application.dataPath, RelativeBurstPath));


        public static void TranslateEtlFile(string etlPath, Stream stream)
        {
            List<string> pdbWhitelist = new List<string>(pdbWhiteList);

            #if UNITY_EDITOR
            var burstDlls = Directory.EnumerateFiles(BurstPath).Where(f => f.EndsWith(".dll")).Select(Path.GetFileName);
            pdbWhitelist.AddRange(burstDlls);
            #endif

            ReadEtlFile(etlPath, pdbWhitelist, out var profTrace, Allocator.Persistent);
            using (var newStackFrames = new NativeList<StackFrameData>(Allocator.TempJob))
            {
                new MergeCallStacksJob
                {
                    NewStackFrames = newStackFrames,
                    Samples = profTrace.Samples,
                    StackFrames = profTrace.StackFrames
                }.Run();
                var tmp = profTrace.StackFrames;
                profTrace.StackFrames = newStackFrames;
                WriteProfilerTrace(ref profTrace, stream);
                profTrace.StackFrames = tmp;
                profTrace.Dispose();
            }
        }
    }
}
