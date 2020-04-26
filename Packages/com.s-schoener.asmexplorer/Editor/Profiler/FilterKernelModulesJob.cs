using System;
using AsmExplorer.Profiler;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace AsmExplorer {
    struct FilterKernelModulesJob
    {
        public NativeArray<SampleData> Samples;
        [ReadOnly]
        public NativeArray<FunctionData> Functions;
        [ReadOnly]
        public NativeArray<ModuleData> Modules;
        [ReadOnly]
        public NativeArray<StackFrameData> StackFrames;

        public unsafe void Run()
        {
            var burstString = new FixedString32("burst");
            var isKernelModule = new NativeArray<bool>(Modules.Length, Allocator.TempJob);
            for (int i = 0; i < Modules.Length; i++)
            {
                isKernelModule[i] = !Modules[i].IsMono && !Modules[i].FilePath.ToString().EndsWith("unity.exe") && !Modules[i].FilePath.Contains(burstString);
            }

            using (isKernelModule)
            using (var functionData = new NativeArray<bool>(Functions.Length, Allocator.TempJob))
            {
                var h = new PropagateModuleToFunctions<bool>
                {
                    Functions = (FunctionData*)Functions.GetUnsafeReadOnlyPtr(),
                    DefaultData = false,
                    ModuleData = (bool*)isKernelModule.GetUnsafeReadOnlyPtr(),
                    FunctionData = (bool*)functionData.GetUnsafePtr()
                }.Schedule(Functions.Length, 32);
                new ReattributeSamples
                {
                    Frames = (StackFrameData*)StackFrames.GetUnsafeReadOnlyPtr(),
                    Samples = (SampleData*)Samples.GetUnsafeReadOnlyPtr(),
                    IgnoreFunction = functionData,
                }.Schedule(Samples.Length, 32, h).Complete();
            }
        }
    }

    unsafe struct PropagateModuleToFunctions<T> : IJobParallelFor where T : unmanaged
    {
        public T DefaultData;
        [ReadOnly]
        [NativeDisableUnsafePtrRestriction]
        public T* ModuleData;
        [NativeDisableUnsafePtrRestriction]
        public T* FunctionData;
        [ReadOnly]
        [NativeDisableUnsafePtrRestriction]
        public FunctionData* Functions;
        public void Execute(int index)
        {
            int module = Functions[index].Module;
            if (module < 0)
                FunctionData[index] = DefaultData;
            else
                FunctionData[index] = ModuleData[module];
        }
    }

    unsafe struct ReattributeSamples : IJobParallelFor
    {
        /// <summary>
        /// Whether a sample in this function should be remapped. That is, whether each sample in that function should
        /// rather be attributed to a function further up the callstack.
        /// </summary>
        [ReadOnly]
        public NativeArray<bool> IgnoreFunction;
        [NativeDisableUnsafePtrRestriction]
        public SampleData* Samples;
        [ReadOnly]
        [NativeDisableUnsafePtrRestriction]
        public StackFrameData* Frames;

        public void Execute(int index)
        {
            int currentFunction = Samples[index].Function;
            int currentStack = Samples[index].StackTrace;
            if (currentStack == -1)
                return;
            while (currentFunction == -1 || IgnoreFunction[currentFunction])
            {
                int caller = Frames[currentStack].CallerStackFrame;
                if (caller == -1)
                    return;
                currentFunction = Frames[currentStack].Function;
                currentStack = caller;
            }
            Samples[index].Function = currentFunction;
        }
    }
}
