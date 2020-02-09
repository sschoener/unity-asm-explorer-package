using System;
using System.Collections.Generic;
using System.IO;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AsmExplorer.Profiler {
    class ScrewItProfiler : EditorWindow
    {
        ProfilerTreeView m_ProfilerTree;
        ProfilerTrace m_Trace;
        FunctionHeatMap m_HeatMap;
        bool m_HasData;
        int m_SelectedThread;
        List<string> m_ThreadNames;
        ToolbarMenu m_ThreadSelection;

        void OnEnable()
        {
            titleContent = new GUIContent("Screw It! Profiler");
            rootVisualElement.style.flexDirection = FlexDirection.Column;

            {
                var toolbar = new Toolbar()
                {
                    style =
                    {
                        height = 16
                    }
                };

                var loadButton = new ToolbarButton();
                loadButton.clicked += ShowLoadTraceFileDialog;
                loadButton.text = "Load Trace";
                toolbar.Add(loadButton);

                m_ThreadNames = new List<string> { "" };
                m_ThreadSelection = new ToolbarMenu
                {
                    variant = ToolbarMenu.Variant.Popup,
                    text = "Select Thread"
                };
                toolbar.Add(m_ThreadSelection);

                rootVisualElement.Add(toolbar);
            }

            {
                // setup header
                var header = new VisualElement
                {
                    style =
                    {
                        height = 20,
                        flexDirection = FlexDirection.Row,
                        flexGrow = 0
                    }
                };

                rootVisualElement.Add(header);
            }

            {
                // setup center
                var center = new VisualElement()
                {
                    style =
                    {
                        flexGrow = 1
                    }
                };

                var mch = new MultiColumnHeader(CreateHeaderState());
                mch.canSort = false;
                m_ProfilerTree = new ProfilerTreeView(new TreeViewState(), mch);
                m_ProfilerTree.Reload();
                var treeContainer = new IMGUIContainer()
                {
                    style =
                    {
                        flexGrow = 1
                    }
                };
                treeContainer.onGUIHandler = () => m_ProfilerTree.OnGUI(treeContainer.contentRect);
                mch.ResizeToFit();
                center.Add(treeContainer);
                rootVisualElement.Add(center);
            }

            {
                // setup footer
                var footer = new VisualElement
                {
                    style =
                    {
                        height = 20,
                        flexDirection = FlexDirection.Row,
                        flexGrow = 0
                    }
                };
                rootVisualElement.Add(footer);
            }
        }

        static readonly string[] k_ProfileFileFilter = { "Profiler Traces", "ptrace" };
        void ShowLoadTraceFileDialog()
        {
            string file = EditorUtility.OpenFilePanelWithFilters("Select trace", Application.dataPath, k_ProfileFileFilter);
            if (string.IsNullOrEmpty(file) || !File.Exists(file))
                return;
            LoadTraceFile(file);
        }

        [BurstCompile]
        struct HeatMapJob : IJob
        {
            public NativeList<FunctionHeatMap.Entry> HeatMap;
            [ReadOnly]
            public ProfilerTrace Trace;
            public int ThreadIndex;

            public void Execute()
            {
                FunctionHeatMap.BuildFromTrace(HeatMap, ref Trace, ThreadIndex);
            }
        }

        void UpdateHeatMap(int threadIndex)
        {
            var heatMap = new NativeList<FunctionHeatMap.Entry>(Allocator.TempJob);
            var job = new HeatMapJob
            {
                Trace = m_Trace,
                ThreadIndex = threadIndex,
                HeatMap = heatMap
            };
            job.Run();
            m_HeatMap.SamplesPerFunction = heatMap.ToArray(Allocator.Persistent);
            heatMap.Dispose();
            m_ProfilerTree.SetData(m_Trace, m_HeatMap);
            m_ProfilerTree.Reload();
        }

        void LoadTraceFile(string path)
        {
            ClearData();

            using (var stream = File.OpenRead(path))
            {
                ProfilerDataSerialization.ReadProfilerTrace(ref m_Trace, stream, Allocator.Persistent);
            }

            m_ThreadNames.Clear();
            for (int i = 0; i < m_Trace.Threads.Length; i++)
            {
                var thread = m_Trace.Threads[i];
                string threadName;
                if (thread.ThreadName.LengthInBytes == 0)
                    threadName = "Thread " + i + " (unnamed)";
                else
                    threadName = thread.ThreadName.ToString();
                m_ThreadNames.Add(threadName);
                int index = i;
                m_ThreadSelection.menu.AppendAction(threadName, action =>
                {
                    m_ThreadSelection.text = action.name;
                    UpdateHeatMap(index);
                });
            }

            m_ThreadSelection.text = m_ThreadNames[0];
            UpdateHeatMap(0);
            m_HasData = true;
        }

        void ClearData()
        {
            if (m_HasData)
            {
                m_Trace.Dispose();
                m_HeatMap.Dispose();
                m_HasData = false;
            }
        }

        void OnDisable()
        {
            ClearData();
        }

        static MultiColumnHeaderState CreateHeaderState() => new MultiColumnHeaderState(new[]
        {
            new MultiColumnHeaderState.Column
            {
                headerContent = new GUIContent("CallStack"),
            },
            new MultiColumnHeaderState.Column
            {
                headerContent = new GUIContent("Total samples"),
            },
            new MultiColumnHeaderState.Column
            {
                headerContent = new GUIContent("Module")
            }
        });
    }
}
