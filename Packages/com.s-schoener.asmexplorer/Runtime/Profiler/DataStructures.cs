using System;
using System.Diagnostics;
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
        public FixedString64 ThreadName;
    }

    struct SampleData : IEquatable<SampleData>
    {
        public int StackTrace;
        public int Function;
        public long Address;
        public double TimeStamp;
        public int ThreadIdx;

        public bool Equals(SampleData other) =>
            StackTrace  == other.StackTrace && Function == other.Function && Address == other.Address && TimeStamp == other.TimeStamp && ThreadIdx == other.ThreadIdx;
    }

    [DebuggerDisplay("Address = {Address}, Function = {Function}, CallerStackFrame = {CallerStackFrame}, Depth = {Depth}")]
    struct StackFrameData : IEquatable<StackFrameData>
    {
        public long Address;
        public int Function;
        public int CallerStackFrame;
        public int Depth;

        public override string ToString() => $"Address = {Address}, Function = {Function}, CallerStackFrame = {CallerStackFrame}, Depth = {Depth}";

        public bool Equals(StackFrameData other) =>
            Address == other.Address && Function == other.Function && CallerStackFrame == other.CallerStackFrame && Depth == other.Depth;
    }

    unsafe struct ModuleData
    {
        public FixedString512 FilePath;
        public FixedString64 PdbName;
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
        public FixedString512 Name;

        public ulong BaseAddress;
        public int Length;
    }
}
