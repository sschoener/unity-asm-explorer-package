using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
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
        List<string> m_ThreadNames;
        PopupField<string> m_ThreadSelection;

        void OnEnable()
        {
            titleContent = new GUIContent("Screw It! Profiler");
            rootVisualElement.style.flexDirection = FlexDirection.Column;

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

                var loadButton = new Button
                {
                    style =
                    {
                        width = 150,
                    }
                };
                loadButton.clicked += ShowLoadTraceFileDialog;
                loadButton.text = "Load Trace";
                header.Add(loadButton);

                m_ThreadNames = new List<string> { "" };
                m_ThreadSelection = new PopupField<string>(m_ThreadNames, 0)
                {
                    style =
                    {
                        width = 150
                    }
                };
                header.Add(m_ThreadSelection);

                var updateButton = new Button
                {
                    style =
                    {
                        width = 150
                    }
                };
                updateButton.clicked += UpdateThread;
                updateButton.text = "Update";
                header.Add(updateButton);

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

        void UpdateThread()
        {
            if (!m_HasData) return;
            m_HeatMap.Dispose();
            UpdateHeatMap(m_ThreadSelection.index);
            m_ProfilerTree.SetData(m_Trace, m_HeatMap);
            m_ProfilerTree.Reload();
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
                unsafe
                {
                    var thread = m_Trace.Threads[i];
                    string threadName;
                    if (thread.ThreadName.LengthInBytes == 0)
                        threadName = "Thread " + i + " (unnamed)";
                    else
                        threadName = thread.ThreadName.ToString();
                    m_ThreadNames.Add(threadName);
                }
            }
            m_ThreadSelection.index = 0;

            UpdateHeatMap(0);
            m_ProfilerTree.SetData(m_Trace, m_HeatMap);
            m_ProfilerTree.Reload();
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
