namespace AsmExplorer.Profiler
{
    unsafe struct ProfilerDataHeader
    {
        public int Version;
        public uint NumSamples;
        public long SamplesOffset => sizeof(ProfilerDataHeader);
        public uint NumFunctions;
        public long FunctionsOffset => SamplesOffset + NumSamples * sizeof(SampleData);
        public uint NumModules;
        public long ModulesOffset => FunctionsOffset + NumFunctions * sizeof(FunctionData);
        public uint NumStackTraces;
        public long StackTracesOffset => ModulesOffset + NumStackTraces + sizeof(StackFrameData);
    }

    struct SampleData
    {
        public int StackTrace;
        public int Function;
        public long Address;
        public double TimeStamp;
        public int ThreadId;
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
        public const int MaxPathLength = 512;
        public const int MaxPdbNameLength = 64;
        public int Checksum;
        public fixed char FilePath[MaxPathLength];
        public fixed byte PdbGuid[16];
        public fixed char PdbName[MaxPathLength];
        public int PdbAge;
    }

    unsafe struct FunctionData
    {
        public const int MaxNameLength = 256;
        public int Module;
        public fixed char Name[MaxNameLength];

        public long BaseAddress;
        public int Length;
    }
}
