using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Unity.Collections.LowLevel.Unsafe;

namespace AsmExplorer.Profiler {
    static class ProfilerDataSerialization
    {
        public static unsafe void RewriteETLFile(string etlPath, string outputPath)
        {
            Dictionary<MethodBase, int> methodIndex = new Dictionary<MethodBase, int>();
            var monoJitData = new List<MonoJitInfo>();
            int nextMethodIndex = -1;
            using (var stream = File.OpenWrite(outputPath))
            using (var writer = new BinaryWriter(stream, Encoding.UTF8))
            using (var reader = TraceLog.OpenOrConvert(etlPath))
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
                        sampleData.ThreadId = (int?)sample.Thread()?.ThreadIndex ?? -1;
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

                {
                    ThreadData threadData = default;
                    foreach (var thread in reader.Threads)
                    {
                        CopyString(threadData.ThreadName, thread.ThreadInfo, ThreadData.MaxThreadNameLength);
                        rawWriter.WriteBytes(&threadData, sizeof(ThreadData));
                    }

                    header.NumThreads = (uint)reader.Threads.Count;
                }

                writer.BaseStream.Position = headerPos;
                rawWriter.WriteBytes(&header, sizeof(ProfilerDataHeader));
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
    }
}
