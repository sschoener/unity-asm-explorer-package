using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEditor.IMGUI.Controls;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AsmExplorer.Profiler
{
    /// <summary>
    /// Zoom in on a hot subtree.
    /// The idea behind this view is that you have found a subtree that contains a lot of the samples, but you do not
    /// know how they are distributed in this subtree. The problem in this situation is that multiple functions in the
    /// hot subtree might be slow for the same reason: They all call a third function that is the actual problem.
    /// The hot subtree view takes all samples whose stack trace contain a specific function (this determines the hot
    /// subtree) and aggregates them by function, both self and total. This allows you to get a better idea how big the
    /// contribution of a function is in that subtree.
    /// </summary>
    class ScrewItHotSubtreeView : IScrewItView
    {
        readonly HotSubtreeTreeView m_SubtreeTreeView;
        readonly ToolbarMenu m_ThreadSelection;
        readonly ToolbarSearchField m_FunctionSearchField;
        string m_CurrentSearchTerm;
        NativeArray<int> m_FunctionWhiteList;
        int m_CurrentThread;
        ProfilerTrace m_Trace;
        NativeArray<SampleData> m_MergedSamples;
        NativeArray<SampleData> m_FilteredSamples;
        NativeList<StackFrameData> m_MergedStackFrames;
        NativeArray<FunctionSampleData> m_FunctionSamples;
        readonly List<VisualElement> m_ToolbarItems = new List<VisualElement>();
        readonly MultiColumnHeader m_ColumnHeader;
        public string Name => "Hot Subtree";

        public VisualElement Root { get; }

        public IEnumerable<VisualElement> ToolbarItems => m_ToolbarItems;

        public ScrewItHotSubtreeView()
        {
            Root = new VisualElement { style = { flexGrow = 1 } };

            m_ThreadSelection = new ToolbarMenu
            {
                text = "Select Thread"
            };
            m_ToolbarItems.Add(m_ThreadSelection);

            m_FunctionSearchField = new ToolbarSearchField();
            m_ToolbarItems.Add(m_FunctionSearchField);

            var updateBtn = new ToolbarButton
            {
                text = "Update",
                style =
                {
                    unityTextAlign = TextAnchor.MiddleLeft
                }
            };
            updateBtn.clicked += () => RefreshData(m_CurrentThread);
            m_ToolbarItems.Add(updateBtn);

            m_ColumnHeader = new MultiColumnHeader(CreateHeaderState())
            {
                canSort = false
            };
            m_SubtreeTreeView = new HotSubtreeTreeView(new TreeViewState(), m_ColumnHeader);
            m_SubtreeTreeView.Reload();
            var treeContainer = new IMGUIContainer { style = { flexGrow = 1 } };
            treeContainer.onGUIHandler = () => m_SubtreeTreeView.OnGUI(treeContainer.contentRect);
            m_ColumnHeader.ResizeToFit();
            Root.Add(treeContainer);
        }

        public void ClearData()
        {
            m_Trace = default;
            m_CurrentThread = -1;
            m_CurrentSearchTerm = null;
            m_MergedStackFrames.TryDispose();
            m_MergedSamples.TryDispose();
            m_FilteredSamples.TryDispose();
            m_FunctionWhiteList.TryDispose();
            m_FunctionSamples.TryDispose();
            m_SubtreeTreeView.ClearData();
            m_SubtreeTreeView.Reload();
        }

        public void OnDisable() { }
        public void OnEnable() { }

        static unsafe NativeArray<int> FindFunctionsByName(string functionName, NativeArray<FunctionData> functions)
        {
            using (var tmpWhiteList = new NativeQueue<int>(Allocator.TempJob))
            {
                new FindFunctionsByName
                {
                    Functions = (FunctionData*)functions.GetUnsafeReadOnlyPtr(),
                    Search = new FixedString128Bytes(functionName),
                    OutFunctions = tmpWhiteList.AsParallelWriter()
                }.Schedule(functions.Length, 32).Complete();
                var arr = new NativeArray<int>(tmpWhiteList.Count, Allocator.TempJob);
                for (int i = 0; i < arr.Length; i++)
                    arr[i] = tmpWhiteList.Dequeue();
                return arr;
            }
        }

        static unsafe void FilterSamplesByFunction(NativeArray<int> functionWhiteList,
            NativeArray<SampleData> samples,
            NativeArray<StackFrameData> stackFrames,
            out NativeArray<SampleData> outSamples)
        {
            if (functionWhiteList.Length == 0)
            {
                outSamples = default;
                return;
            }

            using (var sampleStream = new UnsafeStream(samples.Length, Allocator.TempJob))
            {
                if (functionWhiteList.Length == 1)
                {
                    new FilterSamplesByFunction<SingleFunctionMatcher>
                    {
                        Frames = (StackFrameData*)stackFrames.GetUnsafeReadOnlyPtr(),
                        Samples = (SampleData*)samples.GetUnsafeReadOnlyPtr(),
                        OutputSamples = sampleStream.AsWriter(),
                        Matcher = new SingleFunctionMatcher { Function = functionWhiteList[0] }
                    }.Schedule(samples.Length, 32).Complete();
                }
                else
                {
                    new FilterSamplesByFunction<MultiFunctionMatcher>
                    {
                        Frames = (StackFrameData*)stackFrames.GetUnsafeReadOnlyPtr(),
                        Samples = (SampleData*)samples.GetUnsafeReadOnlyPtr(),
                        OutputSamples = sampleStream.AsWriter(),
                        Matcher = new MultiFunctionMatcher
                        {
                            Functions = (int*)functionWhiteList.GetUnsafeReadOnlyPtr(),
                            NumFunctions = functionWhiteList.Length
                        }
                    }.Schedule(samples.Length, 32).Complete();
                }

                outSamples = sampleStream.ToNativeArray<SampleData>(Allocator.TempJob);
            }
        }

        unsafe struct MultiFunctionMatcher : IFunctionFilter
        {
            [NativeDisableUnsafePtrRestriction]
            public int* Functions;
            public int NumFunctions;

            public bool Match(int function)
            {
                for (int i = 0; i < NumFunctions; i++)
                {
                    if (Functions[i] == function)
                        return true;
                }
                return false;
            }
        }

        struct SingleFunctionMatcher : IFunctionFilter
        {
            public int Function;
            public bool Match(int function) => Function == function;
        }

        unsafe void RefreshData(int threadIndex)
        {
            if (m_FunctionSearchField.value == m_CurrentSearchTerm && m_CurrentThread == threadIndex)
                return;
            if (threadIndex < 0)
                return;

            if (!m_MergedStackFrames.IsCreated)
            {
                m_MergedSamples = new NativeArray<SampleData>(m_Trace.Samples, Allocator.Persistent);
                m_MergedStackFrames = new NativeList<StackFrameData>(Allocator.Persistent);
                new MergeCallStacksJob
                {
                    MergeBy = MergeCallStacksJob.MergeMode.ByFunction,
                    NewStackFrames = m_MergedStackFrames,
                    Samples = m_MergedSamples,
                    StackFrames = m_Trace.StackFrames
                }.Run();
            }

            if (m_FunctionSearchField.value != m_CurrentSearchTerm)
            {
                m_CurrentSearchTerm = m_FunctionSearchField.value;
                m_FilteredSamples.TryDispose();
                m_FunctionWhiteList.TryDispose();
                m_FunctionWhiteList = FindFunctionsByName(m_FunctionSearchField.value, m_Trace.Functions);
                FilterSamplesByFunction(m_FunctionWhiteList, m_MergedSamples, m_MergedStackFrames, out m_FilteredSamples);
            }

            if (m_FunctionWhiteList.Length == 0)
            {
                m_SubtreeTreeView.ClearData();
                return;
            }

            var threadCollection = new CollectThreadStackFrames
            {
                Thread = threadIndex,
                Samples = m_FilteredSamples,
                StackFrames = m_MergedStackFrames,
                FramesInThread = new NativeList<StackFrameSamples>(Allocator.TempJob),
                SamplesInThread = new NativeList<SampleData>(Allocator.TempJob)
            };
            threadCollection.Run();
            threadCollection.SamplesInThread.Dispose();

            if (m_FunctionWhiteList.Length == 1)
            {
                new FilterFramesByFunctionJob<SingleFunctionMatcher>
                {
                    Frames = threadCollection.FramesInThread.AsArray(),
                    Matcher = new SingleFunctionMatcher { Function = m_FunctionWhiteList[0] }
                }.Schedule().Complete();
            }
            else if (m_FunctionWhiteList.IsCreated)
            {
                new FilterFramesByFunctionJob<MultiFunctionMatcher>
                {
                    Frames = threadCollection.FramesInThread.AsArray(),
                    Matcher = new MultiFunctionMatcher
                    {
                        Functions = (int*)m_FunctionWhiteList.GetUnsafeReadOnlyPtr(),
                        NumFunctions = m_FunctionWhiteList.Length
                    }
                }.Schedule().Complete();
            }

            var stream = new UnsafeStream(threadCollection.FramesInThread.Length, Allocator.TempJob);
            new CollectSamplesJob
            {
                Frames = (StackFrameSamples*)threadCollection.FramesInThread.GetUnsafeReadOnlyPtr(),
                OutSamples = stream.AsWriter()
            }.Schedule(threadCollection.FramesInThread.Length, 32).Complete();

            var aggregateJob = new AggregateSamplesJob
            {
                SamplesIn = stream.ToNativeArray<FunctionSampleData>(Allocator.TempJob),
                SamplesOut = new NativeHashMap<int, FunctionSampleData>(threadCollection.FramesInThread.Length, Allocator.TempJob)
            };
            threadCollection.FramesInThread.Dispose();
            aggregateJob.Run();

            var values = aggregateJob.SamplesOut.GetValueArray(Allocator.Persistent);
            aggregateJob.SamplesIn.Dispose();
            aggregateJob.SamplesOut.Dispose();
            NativeSortExtension.Sort(values, new FunctionByTotal());
            m_FunctionSamples.TryDispose();
            m_FunctionSamples = values;

            m_SubtreeTreeView.SetData(ref m_Trace, m_FunctionSamples);
        }

        struct FunctionByTotal : IComparer<FunctionSampleData>
        {
            public int Compare(FunctionSampleData x, FunctionSampleData y) => y.Total.CompareTo(x.Total);
        }

        [BurstCompile]
        unsafe struct FilterFramesByFunctionJob<T> : IJob where T : IFunctionFilter
        {
            public T Matcher;
            public NativeArray<StackFrameSamples> Frames;

            public void Execute()
            {
                var ptr = (StackFrameSamples*)Frames.GetUnsafeReadOnlyPtr();
                for (int i = 0, n = Frames.Length; i < n; i++)
                {
                    // check whether our function matches or whether our parents function matches
                    var f = ptr[i].FrameData.Function;
                    if (!Matcher.Match(f))
                    {
                        int caller = ptr[i].FrameData.CallerStackFrame;
                        if (caller == -1)
                            ptr[i].NumSamplesSelf = -1;
                        else if (ptr[caller].NumSamplesSelf == -1)
                            ptr[i].NumSamplesSelf = -1;
                    }
                }
            }
        }

        [BurstCompile]
        unsafe struct CollectSamplesJob : IJobParallelFor
        {
            public UnsafeStream.Writer OutSamples;
            [ReadOnly]
            [NativeDisableUnsafePtrRestriction]
            public StackFrameSamples* Frames;

            public void Execute(int index)
            {
                // Collect the right samples:
                //  - if we have our self-samples set to -1, we have been filtered our
                //  - if we have a parent that has not been filtered out and shares our function, we only write out our
                //    self value, not the total; this deals with recursive functions
                //  - if we are the topmost frames of that function, write out self and total
                if (Frames[index].NumSamplesSelf == -1)
                    return;
                int targetFunction = Frames[index].FrameData.Function;
                if (targetFunction == -1)
                    return;
                int nextFrame = Frames[index].FrameData.CallerStackFrame;
                bool parentFound = false;
                while (nextFrame != -1)
                {
                    if (Frames[nextFrame].NumSamplesSelf == -1)
                        break;
                    int func = Frames[nextFrame].FrameData.Function;
                    if (func == targetFunction)
                    {
                        parentFound = true;
                        break;
                    }

                    nextFrame = Frames[nextFrame].FrameData.CallerStackFrame;
                }

                OutSamples.BeginForEachIndex(index);
                OutSamples.Write(new FunctionSampleData
                {
                    Function = targetFunction,
                    Self = Frames[index].NumSamplesSelf,
                    Total = parentFound ? 0 : Frames[index].NumSamplesTotal
                });
                OutSamples.EndForEachIndex();
            }
        }

        [BurstCompile]
        unsafe struct AggregateSamplesJob : IJob
        {
            [ReadOnly]
            public NativeArray<FunctionSampleData> SamplesIn;
            public NativeHashMap<int, FunctionSampleData> SamplesOut;

            public void Execute()
            {
                var ptr = (FunctionSampleData*)SamplesIn.GetUnsafeReadOnlyPtr();
                for (int i = 0, n = SamplesIn.Length; i < n; i++)
                {
                    SamplesOut.TryGetValue(ptr[i].Function, out var samples);
                    samples.Function = ptr[i].Function;
                    samples.Self += ptr[i].Self;
                    samples.Total += ptr[i].Total;
                    SamplesOut[samples.Function] = samples;
                }
            }
        }

        public void SetData(ref ProfilerTrace trace)
        {
            m_Trace = trace;

            m_ThreadSelection.menu.MenuItems().Clear();
            m_ThreadSelection.SetEnabled(true);
            m_CurrentThread = -1;

            for (int i = 0; i < m_Trace.Threads.Length; i++)
            {
                var thread = m_Trace.Threads[i];
                string threadName;
                if (thread.ThreadName.Length == 0)
                    threadName = "Thread " + i + " (unnamed)";
                else
                    threadName = thread.ThreadName.ToString();
                int index = i;
                m_ThreadSelection.menu.AppendAction(threadName, action =>
                {
                    m_ThreadSelection.text = action.name;
                    m_CurrentThread = index;
                });
            }

            m_ThreadSelection.text = "Select thread";
        }

        static MultiColumnHeaderState CreateHeaderState() => new MultiColumnHeaderState(new[]
        {
            new MultiColumnHeaderState.Column
            {
                headerContent = new GUIContent("Function name"),
                width = 600
            },
            new MultiColumnHeaderState.Column
            {
                headerContent = new GUIContent("Module"),
                width = 100,
                minWidth = 100
            },
            new MultiColumnHeaderState.Column
            {
                headerContent = new GUIContent("Address"),
                width = 140,
                minWidth = 140,
                maxWidth = 140,
            },
            new MultiColumnHeaderState.Column
            {
                headerContent = new GUIContent("Self"),
                width = 70,
                minWidth = 70,
                maxWidth = 70,
            },
            new MultiColumnHeaderState.Column
            {
                headerContent = new GUIContent("Total"),
                width = 70,
                minWidth = 70,
                maxWidth = 70,
            }
        });
    }

    struct FunctionSampleData
    {
        public int Function;
        public int Total;
        public int Self;
    }

    [BurstCompile]
    unsafe struct FindFunctionsByName : IJobParallelFor
    {
        public FixedString128Bytes Search;
        [NativeDisableUnsafePtrRestriction]
        public FunctionData* Functions;
        public NativeQueue<int>.ParallelWriter OutFunctions;

        public void Execute(int index)
        {
            if (Functions[index].Name.Contains(Search))
                OutFunctions.Enqueue(index);
        }
    }

    interface IFunctionFilter
    {
        bool Match(int function);
    }

    [BurstCompile]
    unsafe struct FilterSamplesByFunction<T> : IJobParallelFor where T : IFunctionFilter
    {
        public T Matcher;

        [ReadOnly]
        [NativeDisableUnsafePtrRestriction]
        public SampleData* Samples;
        [ReadOnly]
        [NativeDisableUnsafePtrRestriction]
        public StackFrameData* Frames;
        public UnsafeStream.Writer OutputSamples;

        public void Execute(int index)
        {
            var samples = Samples[index];
            int function = samples.Function;
            int stack = samples.StackTrace;
            while (!Matcher.Match(function))
            {
                if (stack == -1)
                    return;
                stack = Frames[stack].CallerStackFrame;
                function = Frames[stack].Function;
            }

            OutputSamples.BeginForEachIndex(index);
            OutputSamples.Write(Samples[index]);
            OutputSamples.EndForEachIndex();
        }
    }
}
