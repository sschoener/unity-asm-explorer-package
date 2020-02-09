using System;
using Unity.Collections;

namespace AsmExplorer.Profiler
{
    struct ProfilerTrace : IDisposable
    {
        public ProfilerTraceHeader Header;
        public NativeArray<SampleData> Samples;
        public NativeArray<FunctionData> Functions;
        public NativeArray<ModuleData> Modules;
        public NativeArray<StackFrameData> StackFrames;
        public NativeArray<ThreadData> Threads;

        public void Dispose()
        {
            Samples.Dispose();
            Functions.Dispose();
            Modules.Dispose();
            StackFrames.Dispose();
            Threads.Dispose();
        }
    }

    unsafe struct ProfilerDataSerializationHeader
    {
        public int Version;
        public int TotalLength;

        public int NumSamples;
        public long SamplesOffset;

        public int NumStackFrames;
        public long StackFramesOffset;

        public int NumFunctions;
        public long FunctionsOffset;

        public int NumModules;
        public long ModulesOffset;

        public int NumThreads;
        public long ThreadsOffset;

    }

    struct ProfilerTraceHeader
    {
        public double SamplingInterval;
        public long SessionStart;
        public long SessionEnd;
    }

    unsafe struct ThreadData
    {
        public NativeString64 ThreadName;
    }

    struct SampleData
    {
        public int StackTrace;
        public int Function;
        public long Address;
        public double TimeStamp;
        public int ThreadIdx;
    }

    struct StackFrameData
    {
        public long Address;
        public int Function;
        public int CallerStackFrame;
        public int Depth;
    }

    unsafe struct ModuleData
    {
        public NativeString512 FilePath;
        public NativeString64 PdbName;
        public ulong ImageBase;
        public ulong ImageEnd;
        public fixed byte PdbGuid[16];
        public int PdbAge;
        public int Checksum;
        public bool IsMono;
    }

    unsafe struct FunctionData
    {
        public int Module;
        public NativeString512 Name;

        public long BaseAddress;
        public int Length;
    }
}
