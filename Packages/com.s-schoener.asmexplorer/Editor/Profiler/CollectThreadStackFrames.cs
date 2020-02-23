using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace AsmExplorer.Profiler
{
    [BurstCompile]
    unsafe struct CollectThreadStackFrames : IJob
    {
        // Inputs
        public int Thread;
        [ReadOnly]
        public NativeArray<SampleData> Samples;
        [ReadOnly]
        public NativeArray<StackFrameData> StackFrames;

        // Outputs
        public NativeList<SampleData> SamplesInThread;
        public NativeList<StackFrameSamples> FramesInThread;

        public void Execute()
        {
            {
                var collectedFrames = new NativeList<FrameSortData>(Allocator.Temp);
                var oldToNewFrameIndex = new NativeHashMap<int, int>(StackFrames.Length, Allocator.Temp);
                var inSamples = (SampleData*)Samples.GetUnsafeReadOnlyPtr();
                var inFrames = (StackFrameData*)StackFrames.GetUnsafeReadOnlyPtr();

                // collect all samples in this thread and remap the stack frame indices
                for (int s = 0, n = Samples.Length; s < n; s++)
                {
                    if (inSamples[s].ThreadIdx == Thread)
                    {
                        int frameIndex = inSamples[s].StackTrace;
                        if (frameIndex != -1 && !oldToNewFrameIndex.TryGetValue(frameIndex, out int _))
                        {
                            oldToNewFrameIndex.Add(frameIndex, -1);
                            collectedFrames.Add(new FrameSortData {
                                Depth = inFrames[frameIndex].Depth,
                                Index = frameIndex
                            });
                        }
                        SamplesInThread.Add(inSamples[s]);
                    }
                }

                // collect all remaining stack frames and remap the caller indices
                // NB this loop adds new entries to FramesInThread during the iteration
                for (int s = 0; s < collectedFrames.Length; s++)
                {
                    int caller = inFrames[collectedFrames[s].Index].CallerStackFrame;
                    if (caller != -1 && !oldToNewFrameIndex.TryGetValue(caller, out int _))
                    {
                        oldToNewFrameIndex.Add(caller, -1);
                        collectedFrames.Add(new FrameSortData {
                            Depth = inFrames[caller].Depth,
                            Index = caller
                        });
                    }
                }

                // sort all frames by their depth so that the lowest depth stack frames are at the beginning
                NativeSortExtension.Sort((FrameSortData*)collectedFrames.GetUnsafePtr(), collectedFrames.Length, new FrameComp());

                // map old indices to new indices
                oldToNewFrameIndex.Clear();
                for (int i = 0; i < collectedFrames.Length; i++)
                    oldToNewFrameIndex.Add(collectedFrames[i].Index, i);

                {
                    // copy stack frames to output and adjust caller indices
                    FramesInThread.ResizeUninitialized(collectedFrames.Length);
                    var outputPtr = (StackFrameSamples*)FramesInThread.GetUnsafePtr();
                    for (int i = 0, n = collectedFrames.Length; i < n; i++) {
                        outputPtr[i] = new StackFrameSamples {
                            FrameData = StackFrames[collectedFrames[i].Index],
                        };
                        ref int caller = ref outputPtr[i].FrameData.CallerStackFrame;
                        if (oldToNewFrameIndex.TryGetValue(caller, out var newCaller))
                            caller = newCaller;
                    }
                }

                {
                    // adjust stack frame references in the samples
                    var outputPtr = (SampleData*)SamplesInThread.GetUnsafePtr();
                    for (int i = 0, n = SamplesInThread.Length; i < n; i++) {
                        ref int trace = ref outputPtr[i].StackTrace;
                        if (oldToNewFrameIndex.TryGetValue(trace, out var newTrace))
                            trace = newTrace;
                    }
                }

                collectedFrames.Dispose();
                oldToNewFrameIndex.Dispose();
            }

            // now sort all samples by their stack frame index
            SamplesInThread.Sort(new SampleComp());
            var outTraces = (StackFrameSamples*)FramesInThread.GetUnsafePtr();

            {
                // and record the number of samples in each stack frame
                var outSamples = (SampleData*)SamplesInThread.GetUnsafeReadOnlyPtr();
                int runStart = -1;
                int currentTrace = -1;
                for (int s = 0, n = SamplesInThread.Length; s < n; s++)
                {
                    int trace = outSamples[s].StackTrace;
                    if (trace != currentTrace)
                    {
                        if (currentTrace != -1)
                        {
                            outTraces[currentTrace].NumSamplesSelf += s - runStart;
                            outTraces[currentTrace].SamplesOffset = runStart;
                        }
                        runStart = s;
                        currentTrace = trace;
                    }
                }
                if (currentTrace != -1)
                {
                    outTraces[currentTrace].NumSamplesSelf += SamplesInThread.Length - runStart;
                    outTraces[currentTrace].SamplesOffset = runStart;
                }
            }

            // finally, accumulate the number of samples within the stack traces
            for (int s = FramesInThread.Length - 1; s >= 0; s--)
            {
                int caller = outTraces[s].FrameData.CallerStackFrame;
                outTraces[s].NumSamplesTotal += outTraces[s].NumSamplesSelf;
                if (caller != -1)
                    outTraces[caller].NumSamplesTotal += outTraces[s].NumSamplesTotal;
            }
        }

        struct SampleComp : IComparer<SampleData>
        {
            public int Compare(SampleData x, SampleData y) => x.StackTrace.CompareTo(y.StackTrace);
        }

        struct FrameSortData
        {
            public int Index;
            public int Depth;
        }

        struct FrameComp : IComparer<FrameSortData>
        {
            public int Compare(FrameSortData x, FrameSortData y) => x.Depth.CompareTo(y.Depth);
        }
    }

    struct StackFrameSamples
    {
        public int NumSamplesTotal;
        public int NumSamplesSelf;
        public int SamplesOffset;
        public StackFrameData FrameData;
    }
}
