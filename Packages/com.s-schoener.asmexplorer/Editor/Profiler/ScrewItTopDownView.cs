using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEditor.IMGUI.Controls;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AsmExplorer.Profiler
{
    class ScrewItTopDownView : IScrewItView
    {
        readonly TopDownTreeView m_TreeView;
        readonly ToolbarMenu m_ThreadSelection;
        ProfilerTrace m_Trace;
        readonly List<TopDownTreeData> m_TreeDataPerThread = new List<TopDownTreeData>();
        readonly List<VisualElement> m_ToolbarItems = new List<VisualElement>();
        readonly MultiColumnHeader m_ColumnHeader;
        public string Name => "Top Down";

        public VisualElement Root { get; }

        public IEnumerable<VisualElement> ToolbarItems => m_ToolbarItems;

        public ScrewItTopDownView()
        {
            Root = new VisualElement { style = { flexGrow = 1 } };

            m_ThreadSelection = new ToolbarMenu
            {
                text = "Select Thread"
            };
            m_ToolbarItems.Add(m_ThreadSelection);

            m_ColumnHeader = new MultiColumnHeader(CreateHeaderState())
            {
                canSort = false
            };
            m_TreeView = new TopDownTreeView(new TreeViewState(), m_ColumnHeader);
            m_TreeView.Reload();
            var treeContainer = new IMGUIContainer { style = { flexGrow = 1 } };
            treeContainer.onGUIHandler = () => m_TreeView.OnGUI(treeContainer.contentRect);
            m_ColumnHeader.ResizeToFit();
            Root.Add(treeContainer);
        }

        public void ClearData()
        {
            m_Trace = default;
            for (int i = 0; i < m_TreeDataPerThread.Count; i++)
                m_TreeDataPerThread[i].Dispose();
            m_TreeDataPerThread.Clear();
        }

        public void OnDisable() { }
        public void OnEnable() { }

        void UpdateTree(int threadIndex)
        {
            if (m_TreeDataPerThread.Count <= threadIndex || !m_TreeDataPerThread[threadIndex].Frames.IsCreated)
            {
                var threadCollection = new CollectThreadStackFrames
                {
                    Thread = threadIndex,
                    Samples = m_Trace.Samples,
                    StackFrames = m_Trace.StackFrames,
                    FramesInThread = new NativeList<StackFrameSamples>(Allocator.TempJob),
                    SamplesInThread = new NativeList<SampleData>(Allocator.TempJob)
                };
                threadCollection.Run();
                var frames = threadCollection.FramesInThread.ToArray(Allocator.Persistent);
                var samples = threadCollection.SamplesInThread.ToArray(Allocator.Persistent);

                threadCollection.FramesInThread.Dispose();
                threadCollection.SamplesInThread.Dispose();

                var computeTreeJob = new ComputeTreeIndices<StackFrameSamples, StackFrameSamplesTree>
                {
                    Data = frames,
                    Tree = new StackFrameSamplesTree(),
                };
                computeTreeJob.AllocateTreeIndex(Allocator.Persistent);
                computeTreeJob.Run();

                for (int i = m_TreeDataPerThread.Count; i <= threadIndex; i++)
                    m_TreeDataPerThread.Add(default);
                m_TreeDataPerThread[threadIndex].Dispose();
                m_TreeDataPerThread[threadIndex] = new TopDownTreeData
                {
                    Frames = frames,
                    Samples = samples,
                    Tree = computeTreeJob.Indices
                };
            }

            m_TreeView.SetData(ref m_Trace, m_TreeDataPerThread[threadIndex]);
        }

        struct StackFrameSamplesTree : ITree<StackFrameSamples>
        {
            public int GetDepth(ref StackFrameSamples data) => data.FrameData.Depth;
            public int GetParent(ref StackFrameSamples data) => data.FrameData.CallerStackFrame;
        }

        public void SetData(ref ProfilerTrace trace)
        {
            m_Trace = trace;

            m_ThreadSelection.menu.MenuItems().Clear();
            m_ThreadSelection.SetEnabled(true);

            for (int i = 0; i < m_Trace.Threads.Length; i++)
            {
                var thread = m_Trace.Threads[i];
                string threadName;
                if (thread.ThreadName.LengthInBytes == 0)
                    threadName = "Thread " + i + " (unnamed)";
                else
                    threadName = thread.ThreadName.ToString();
                int index = i;
                m_ThreadSelection.menu.AppendAction(threadName, action =>
                {
                    m_ThreadSelection.text = action.name;
                    UpdateTree(index);
                });
            }

            m_ThreadSelection.text = "Select thread";
        }

        static MultiColumnHeaderState CreateHeaderState() => new MultiColumnHeaderState(new[]
        {
            new MultiColumnHeaderState.Column
            {
                headerContent = new GUIContent("Function"),
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
                headerContent = new GUIContent("Total"),
                width = 50,
                minWidth = 50,
                maxWidth = 50,
            },
            new MultiColumnHeaderState.Column
            {
                headerContent = new GUIContent("Self"),
                width = 50,
                minWidth = 50,
                maxWidth = 50,
            },
        });
    }

    struct TopDownTreeData : IDisposable
    {
        public NativeArray<SampleData> Samples;
        public NativeArray<StackFrameSamples> Frames;
        public TreeIndex Tree;

        public void Dispose()
        {
            if (Samples.IsCreated)
                Samples.Dispose();
            if (Frames.IsCreated)
                Frames.Dispose();
            Tree.Dispose();
        }
    }
}
