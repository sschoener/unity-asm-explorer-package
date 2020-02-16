using System;
using System.IO;
using NUnit.Framework;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace AsmExplorer.Profiler.Tests
{
    public class CollectThreadStackFramesTests
    {
        static void RunCollect(NativeArray<SampleData> samples, NativeArray<StackFrameData> stackFrames,
            out NativeList<SampleData> outSamples, out NativeList<StackFrameSamples> outStackFrames, int threadIndex = 0,
            Allocator allocator = Allocator.TempJob)
        {
            outStackFrames = new NativeList<StackFrameSamples>(allocator);
            outSamples = new NativeList<SampleData>(allocator);
            var job = new CollectThreadStackFrames
            {
                FramesInThread = outStackFrames,
                SamplesInThread = outSamples,
                Thread = threadIndex,
                Samples = samples,
                StackFrames = stackFrames
            };
            job.Run();
        }

        unsafe void LoadTestData(string path, out NativeArray<SampleData> samples, out NativeArray<StackFrameData> stackFrames, Allocator allocator = Allocator.Persistent)
        {
            using (var stream = File.OpenRead(path)) {
                var reader = new RawReader(stream);

                int numSamples = 0;
                reader.Read(&numSamples);
                samples = new NativeArray<SampleData>(numSamples, allocator);
                reader.ReadBytes(samples.GetUnsafePtr(), numSamples * sizeof(SampleData));

                int numStackFrames = 0;
                reader.Read(&numStackFrames);
                stackFrames = new NativeArray<StackFrameData>(numStackFrames, allocator);
                reader.ReadBytes(stackFrames.GetUnsafePtr(), numStackFrames * sizeof(StackFrameData));
            }
        }

        [Test]
        public void EmptyIsNoop()
        {
            using (var samples = new NativeArray<SampleData>(0, Allocator.TempJob))
            using (var frames = new NativeArray<StackFrameData>(0, Allocator.TempJob))
            {
                RunCollect(samples, frames, out var outSamples, out var outFrames);
                using (outFrames)
                using (outSamples)
                {
                    Assert.AreEqual(0, outSamples.Length);
                    Assert.AreEqual(0, outFrames.Length);
                }
            }
        }

        static readonly SampleData[] s_TestSamples0 = {
            new SampleData {
                Address = 1,
                StackTrace = 1,
                ThreadIdx = 0,
                Function = 1
            },
            new SampleData {
                Address = 2,
                StackTrace = 1,
                ThreadIdx = 0,
                Function = 1
            },
            new SampleData {
                Address = 1,
                StackTrace = 0,
                ThreadIdx = 1,
                Function = 1
            },
            new SampleData {
                Address = 5,
                StackTrace = -1,
                ThreadIdx = 0,
                Function = 1
            }
        };
        static readonly StackFrameData[] s_TestFrames0 = {
            new StackFrameData {
                Address = 2,
                CallerStackFrame = -1,
                Depth = 1,
                Function = 7,
            },
            new StackFrameData {
                Address = 1,
                CallerStackFrame = 2,
                Depth = 2,
                Function = 1,
            },
            new StackFrameData {
                Address = 5,
                CallerStackFrame = -1,
                Depth = 1,
                Function = 2,
            }
        };

        static void AssertSortedByFrameIndex(NativeList<SampleData> samples)
        {
            int lastIndex = -1;
            for (int s = 0, n = samples.Length; s < n; s++)
            {
                Assert.GreaterOrEqual(samples[s].StackTrace, lastIndex, $"Samples not sorted at index {s}");
                lastIndex = samples[s].StackTrace;
            }
        }

        static void AssertSampleOffsetsIntact(NativeList<SampleData> samples, NativeList<StackFrameSamples> frames)
        {
            for (int f = 0, n = frames.Length; f < n; f++)
            {
                int numSamples = frames[f].NumSamplesSelf;
                int sampleOffsets = frames[f].SamplesOffset;
                Assert.LessOrEqual(sampleOffsets + numSamples, samples.Length, $"Sample indices invalid at index {f}");
                Assert.GreaterOrEqual(numSamples, 0, $"Number of samples invalid at index {f}");
                Assert.GreaterOrEqual(sampleOffsets, 0, $"Sample offset invalid at index {f}");
                for (int s = 0; s < numSamples; s++)
                {
                    int o = sampleOffsets + s;
                    Assert.AreEqual(samples[o].StackTrace, f, $"Sample {o} does not point to stack trace {f}.");
                }
            }
        }

        static void AssertSampleCountsInCallStack(NativeList<StackFrameSamples> frames)
        {
            for (int f = 0, n = frames.Length; f < n; f++)
            {
                Assert.GreaterOrEqual(frames[f].NumSamplesSelf, 0);
                Assert.GreaterOrEqual(frames[f].NumSamplesTotal, 0);
                Assert.GreaterOrEqual(frames[f].NumSamplesTotal, frames[f].NumSamplesSelf);
                int caller = frames[f].FrameData.CallerStackFrame;
                Assert.Less(caller, frames.Length);
                if (caller != -1)
                {
                    Assert.GreaterOrEqual(frames[caller].NumSamplesTotal, frames[f].NumSamplesTotal);
                }
            }
        }

        static int SamplesInThreadWithStackTrace(NativeArray<SampleData> samples, int thread) {
            int count = 0;
            for (int s = 0, n = samples.Length; s < n; s++) {
                if (samples[s].StackTrace < 0) continue;
                if (samples[s].ThreadIdx != thread) continue;
                count++;
            }
            return count;
        }

        static int TotalSelfSamples(NativeList<StackFrameSamples> frames)
        {
            int sum = 0;
            for (int f = 0, n = frames.Length; f < n; f++)
                sum += frames[f].NumSamplesSelf;
            return sum;
        }

        static int TotalSamples(NativeList<StackFrameSamples> frames)
        {
            int sum = 0;
            for (int f = 0, n = frames.Length; f < n; f++)
            {
                if (frames[f].FrameData.CallerStackFrame == -1)
                    sum += frames[f].NumSamplesTotal;
            }
            return sum;
        }

        static void ForEach<T>(NativeList<T> list, Action<T> act) where T : unmanaged
        {
            for (int i = 0, n = list.Length; i < n; i++)
                act(list[i]);
        }

        [Test]
        public void SingleThreadDoesNotDropSamples()
        {
            using (var samples = new NativeArray<SampleData>(s_TestSamples0, Allocator.TempJob))
            using (var frames = new NativeArray<StackFrameData>(s_TestFrames0, Allocator.TempJob))
            {
                unsafe
                {
                    var ptr = (SampleData*)samples.GetUnsafePtr();
                    for (int s = 0; s < samples.Length; s++)
                        ptr[s].ThreadIdx = 0;
                }
                RunCollect(samples, frames, out var outSamples, out var outFrames);
                using (outSamples)
                using (outFrames)
                {
                    Assert.AreEqual(samples.Length, outSamples.Length);
                    Assert.AreEqual(frames.Length, outFrames.Length);
                    AssertSortedByFrameIndex(outSamples);
                    AssertSampleOffsetsIntact(outSamples, outFrames);
                    AssertSampleCountsInCallStack(outFrames);
                    int totalSelf = TotalSelfSamples(outFrames);
                    Assert.AreEqual(SamplesInThreadWithStackTrace(samples, 0), totalSelf);
                    Assert.AreEqual(totalSelf, TotalSamples(outFrames));

                    ForEach(outSamples, s => Assert.Less(s.StackTrace, outFrames.Length));
                }
            }
        }

        [Test]
        public void UnreachableStackFramesAreDropped()
        {
            using (var samples = new NativeArray<SampleData>(1, Allocator.TempJob))
            using (var frames = new NativeArray<StackFrameData>(2, Allocator.TempJob))
            {
                RunCollect(samples, frames, out var outSamples, out var outFrames);
                using (outSamples)
                using (outFrames)
                {
                    Assert.AreEqual(samples.Length, outSamples.Length);
                    Assert.AreEqual(1, outFrames.Length);
                }
            }
        }


        static readonly SampleData[] s_TestSamples1 = {
            new SampleData {
                StackTrace = 1,
            },
            new SampleData {
                StackTrace = 4,
            },
        };
        static readonly StackFrameData[] s_TestFrames1 = {
            new StackFrameData {
                CallerStackFrame = 2,
                Depth = 3,
            },
            new StackFrameData {
                CallerStackFrame = 2,
                Depth = 3,
            },
            new StackFrameData {
                CallerStackFrame = 3,
                Depth = 2,
            },
            new StackFrameData {
                CallerStackFrame = -1,
                Depth = 1,
            },
            new StackFrameData {
                CallerStackFrame = 0,
                Depth = 4,
            },
        };

        [Test]
        public void SamplesAccumulateOverMultipleLevels(){
            using (var samples = new NativeArray<SampleData>(s_TestSamples1, Allocator.TempJob))
            using (var frames = new NativeArray<StackFrameData>(s_TestFrames1, Allocator.TempJob))
            {
                RunCollect(samples, frames, out var outSamples, out var outFrames);
                using (outSamples)
                using (outFrames)
                {
                    Assert.AreEqual(samples.Length, outSamples.Length);
                    Assert.AreEqual(frames.Length, outFrames.Length);
                    AssertSortedByFrameIndex(outSamples);
                    AssertSampleOffsetsIntact(outSamples, outFrames);
                    AssertSampleCountsInCallStack(outFrames);
                    int totalSelf = TotalSelfSamples(outFrames);
                    Assert.AreEqual(2, totalSelf);
                    Assert.AreEqual(SamplesInThreadWithStackTrace(samples, 0), totalSelf);
                    Assert.AreEqual(totalSelf, TotalSamples(outFrames));

                    ForEach(outSamples, s => Assert.Less(s.StackTrace, outFrames.Length));
                }
            }
        }

        [Test]
        public void IntegrationTest()
        {
            using (var samples = new NativeArray<SampleData>(s_TestSamples0, Allocator.TempJob))
            using (var frames = new NativeArray<StackFrameData>(s_TestFrames0, Allocator.TempJob))
            {
                RunCollect(samples, frames, out var outSamples, out var outFrames);
                using (outSamples)
                using (outFrames)
                {
                    Assert.AreEqual(samples.Length - 1, outSamples.Length);
                    Assert.AreEqual(frames.Length - 1, outFrames.Length);
                    AssertSortedByFrameIndex(outSamples);
                    AssertSampleOffsetsIntact(outSamples, outFrames);
                    AssertSampleCountsInCallStack(outFrames);
                    int totalSelf = TotalSelfSamples(outFrames);
                    Assert.AreEqual(SamplesInThreadWithStackTrace(samples, 0), totalSelf);
                    Assert.AreEqual(totalSelf, TotalSamples(outFrames));

                    ForEach(outSamples, s => Assert.Less(s.StackTrace, outFrames.Length));
                }
            }
        }
    }
}