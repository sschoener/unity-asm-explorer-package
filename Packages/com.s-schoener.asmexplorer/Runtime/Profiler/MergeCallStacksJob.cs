using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace AsmExplorer.Profiler
{
    [BurstCompile]
    struct MergeCallStacksJob : IJob
    {
        [ReadOnly]
        public NativeArray<StackFrameData> StackFrames;
        public NativeArray<SampleData> Samples;
        public NativeList<StackFrameData> NewStackFrames;
        public MergeMode MergeBy;

        public enum MergeMode {
            ByAddress,
            ByFunction
        }

        public unsafe void Execute()
        {
            if (StackFrames.Length == 0)
                return;
            // sort all frames by depth and function
            int numStackFrames = StackFrames.Length;
            var sortData = new NativeArray<SortData>(numStackFrames, Allocator.Temp);
            var ptr = (SortData*)sortData.GetUnsafePtr();
            var frames = (StackFrameData*)StackFrames.GetUnsafeReadOnlyPtr();
            for (int i = 0, n = numStackFrames; i < n; i++)
            {
                ptr[i].Index = i;
                ptr[i].Depth = frames[i].Depth;
                ptr[i].Location = MergeBy == MergeMode.ByAddress ? frames[i].Address : frames[i].Function;
                ptr[i].Caller = frames[i].CallerStackFrame;
            }
            NativeSortExtension.Sort(ptr, numStackFrames, new SortByDepth());
            var remapping = new NativeArray<int>(numStackFrames, Allocator.Temp);

            NewStackFrames.Capacity = StackFrames.Length;
            // process one depth level at a time
            int currentIndex = 0;
            int depth = 1;
            while (currentIndex < numStackFrames) {
                int startIndex = currentIndex;
                if (depth > 1) {
                    // patch caller
                    while (currentIndex < numStackFrames && ptr[currentIndex].Depth == depth) {
                        ptr[currentIndex].Caller = remapping[ptr[currentIndex].Caller];
                        currentIndex++;
                    }
                } else {
                    while (currentIndex < numStackFrames && ptr[currentIndex].Depth == depth)
                        currentIndex++;
                }
                int numOnLevel = currentIndex - startIndex;
                // on depth 1, every frame has an invalid parent, so there's no need to sort
                if (depth > 1)
                    NativeSortExtension.Sort(ptr + startIndex, numOnLevel, new SortByCaller());
                
                {
                    // always copy over the first frame on each depth level
                    ref var tmp = ref ptr[startIndex];
                    var frame = frames[tmp.Index];
                    frame.CallerStackFrame = tmp.Caller;
                    remapping[tmp.Index] = NewStackFrames.Length;
                    NewStackFrames.Add(frame);
                }

                for (int i = startIndex + 1; i < currentIndex; i++) {
                    ref var fst = ref ptr[i - 1];
                    ref var snd = ref ptr[i];
                    if (fst.Caller != snd.Caller || fst.Location != snd.Location) {
                        var frame = frames[snd.Index];
                        frame.CallerStackFrame = snd.Caller;
                        NewStackFrames.Add(frame);
                    }
                    remapping[snd.Index] = NewStackFrames.Length - 1;
                }
                depth += 1;
            }

            // remap the samples
            {
                var samplePtr = (SampleData*)Samples.GetUnsafePtr();
                var remappingPtr = (int*)remapping.GetUnsafeReadOnlyPtr();
                for (int i = 0, n = Samples.Length; i < n; i++) {
                    if (samplePtr[i].StackTrace != -1)
                        samplePtr[i].StackTrace = remappingPtr[samplePtr[i].StackTrace];
                }
            }

            remapping.Dispose();
            sortData.Dispose();
        }

        struct SortByDepth : IComparer<SortData>
        {
            public int Compare(SortData x, SortData y)
            {
                if (x.Depth < y.Depth)
                    return -1;
                if (x.Depth > y.Depth)
                    return 1;
                return x.Location.CompareTo(y.Location);
            }
        }

        struct SortByCaller : IComparer<SortData>
        {
            public int Compare(SortData x, SortData y) {
                if (x.Caller < y.Caller)
                    return -1;
                if (x.Caller > y.Caller)
                    return 1;
                return x.Location.CompareTo(y.Location);
            }
        }

        struct SortData
        {
            public int Index;
            public int Depth;
            public long Location;
            public int Caller;
        }
    }
}
