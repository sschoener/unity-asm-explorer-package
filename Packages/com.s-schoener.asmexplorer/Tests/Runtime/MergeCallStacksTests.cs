using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;

namespace AsmExplorer.Profiler.Tests
{
    public class MergeCallStacksTests
    {
        static NativeList<StackFrameData> RunMerge(NativeArray<SampleData> samples, NativeArray<StackFrameData> stackFrames)
        {
            var newStackFrames = new NativeList<StackFrameData>(Allocator.TempJob);
            new MergeCallStacksJob
            {
                NewStackFrames = newStackFrames,
                Samples = samples,
                StackFrames = stackFrames
            }.Run();
            return newStackFrames;
        }

        [Test]
        public static void DifferentAddressesAreNotMerged()
        {
            using (var samples = new NativeArray<SampleData>(0, Allocator.TempJob))
            using (var frames = new NativeArray<StackFrameData>(new[] {
                    new StackFrameData {
                        Address = 0,
                        CallerStackFrame = -1,
                        Depth = 1
                    },
                    new StackFrameData {
                        Address = 1,
                        CallerStackFrame = -1,
                        Depth = 1
                    }
                }, Allocator.TempJob))
            {
                using (var results = RunMerge(samples, frames)) {
                    Assert.AreEqual(2, results.Length);
                    Assert.AreEqual(frames[0], results[0]);
                    Assert.AreEqual(frames[1], results[1]);
                }
            }
        }

        [Test]
        public static void SameAddressesAreMerged()
        {
            using (var samples = new NativeArray<SampleData>(0, Allocator.TempJob))
            using (var frames = new NativeArray<StackFrameData>(new[] {
                    new StackFrameData {
                        Address = 0,
                        CallerStackFrame = -1,
                        Depth = 1
                    },
                    new StackFrameData {
                        Address = 0,
                        CallerStackFrame = -1,
                        Depth = 1
                    }
                }, Allocator.TempJob))
            {
                using (var results = RunMerge(samples, frames)) {
                    Assert.AreEqual(1, results.Length);
                    Assert.AreEqual(frames[0], results[0]);
                }
            }
        }

        [Test]
        public static void SameAddressesAreNotMergedForDifferentDepths()
        {
            using (var samples = new NativeArray<SampleData>(0, Allocator.TempJob))
            using (var frames = new NativeArray<StackFrameData>(new[] {
                    new StackFrameData {
                        Address = 0,
                        CallerStackFrame = -1,
                        Depth = 1
                    },
                    new StackFrameData {
                        Address = 0,
                        CallerStackFrame = 0,
                        Depth = 2
                    },
                }, Allocator.TempJob))
            {
                using (var results = RunMerge(samples, frames)) {
                    Assert.AreEqual(2, results.Length);
                    Assert.AreEqual(frames[0], results[0]);
                    Assert.AreEqual(frames[1], results[1]);
                }
            }
        }

        [Test]
        public static void DeepStacksAreMerged()
        {
            using (var samples = new NativeArray<SampleData>(0, Allocator.TempJob))
            using (var frames = new NativeArray<StackFrameData>(new[] {
                    new StackFrameData {
                        Address = 0,
                        CallerStackFrame = -1,
                        Depth = 1
                    },
                    new StackFrameData {
                        Address = 1,
                        CallerStackFrame = 0,
                        Depth = 2
                    },
                    new StackFrameData {
                        Address = 2,
                        CallerStackFrame = 1,
                        Depth = 3
                    },
                    new StackFrameData {
                        Address = 0,
                        CallerStackFrame = -1,
                        Depth = 1
                    },
                    new StackFrameData {
                        Address = 1,
                        CallerStackFrame = 3,
                        Depth = 2
                    },
                    new StackFrameData {
                        Address = 2,
                        CallerStackFrame = 4,
                        Depth = 3
                    }
                }, Allocator.TempJob))
            {
                using (var results = RunMerge(samples, frames)) {
                    Assert.AreEqual(3, results.Length);
                    Assert.AreEqual(frames[0], results[0]);
                    Assert.AreEqual(frames[1], results[1]);
                    Assert.AreEqual(frames[2], results[2]);
                }
            }
        }

        [Test]
        public static void StacksArePartiallyMerged()
        {
            using (var samples = new NativeArray<SampleData>(new[] {
                new SampleData {
                    StackTrace = 5
                }
            }, Allocator.TempJob))
            using (var frames = new NativeArray<StackFrameData>(new[] {
                    new StackFrameData {
                        Address = 0,
                        CallerStackFrame = -1,
                        Depth = 1
                    },
                    new StackFrameData {
                        Address = 1,
                        CallerStackFrame = 0,
                        Depth = 2
                    },
                    new StackFrameData {
                        Address = 2,
                        CallerStackFrame = 1,
                        Depth = 3
                    },
                    new StackFrameData {
                        Address = 0,
                        CallerStackFrame = -1,
                        Depth = 1
                    },
                    new StackFrameData {
                        Address = 1,
                        CallerStackFrame = 3,
                        Depth = 2
                    },
                    new StackFrameData {
                        Address = 4,
                        CallerStackFrame = 4,
                        Depth = 3
                    }
                }, Allocator.TempJob))
            {
                using (var results = RunMerge(samples, frames)) {
                    Assert.AreEqual(4, results.Length);
                    Assert.AreEqual(frames[0], results[0]);
                    Assert.AreEqual(frames[1], results[1]);
                    Assert.AreEqual(frames[2], results[2]);
                    Assert.AreEqual(frames[5].Address, results[3].Address);
                    Assert.AreEqual(frames[5].Depth, results[3].Depth);
                    Assert.AreEqual(frames[5].Function, results[3].Function);

                    Assert.AreEqual(3, samples[0].StackTrace);
                }
            }
        }
    }
}