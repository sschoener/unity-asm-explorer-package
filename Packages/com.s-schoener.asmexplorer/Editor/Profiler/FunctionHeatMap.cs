using System;
using AsmExplorer.Profiler;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace AsmExplorer {
    struct FunctionHeatMap : IDisposable
    {
        public NativeArray<Entry> SamplesPerFunction;

        public static unsafe void BuildFromTrace(NativeList<Entry> samplesPerFunction, NativeArray<SampleData> samples, int threadIdx)
        {
            samplesPerFunction.Clear();
            var functionIndex = new NativeHashMap<int, int>(samplesPerFunction.Length, Allocator.Temp);
            var samplePtr = (SampleData*)samples.GetUnsafeReadOnlyPtr();
            for (int i = 0, n = samples.Length; i < n; i++)
            {
                if (samplePtr[i].ThreadIdx != threadIdx)
                    continue;
                if (!functionIndex.TryGetValue(samplePtr[i].Function, out int funcIdx))
                {
                    funcIdx = samplesPerFunction.Length;
                    functionIndex.Add(samplePtr[i].Function, funcIdx);
                    samplesPerFunction.Add(new Entry { Function = samplePtr[i].Function });

                }
                var samplesPerFunctionPtr = (Entry*)samplesPerFunction.GetUnsafePtr();
                samplesPerFunctionPtr[funcIdx].Samples += 1;
            }

            var output = (Entry*)samplesPerFunction.GetUnsafePtr();
            NativeSortExtension.Sort(output, samplesPerFunction.Length);
        }

        public struct Entry : IComparable<Entry>
        {
            public int Function;
            public int Samples;

            public int CompareTo(Entry other) => -Samples.CompareTo(other.Samples);
        }

        public void Dispose()
        {
            SamplesPerFunction.Dispose();
        }
    }
}
