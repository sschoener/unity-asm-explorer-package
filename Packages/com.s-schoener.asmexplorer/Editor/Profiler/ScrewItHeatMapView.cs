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

namespace AsmExplorer.Profiler
{
    class ScrewItHeatMapView : IScrewItView
    {
        readonly HeatMapTreeView m_HeatMapTreeView;
        readonly ToolbarMenu m_ThreadSelection;
        FunctionHeatMap m_HeatMap;
        ProfilerTrace m_Trace;
        ToolbarButton m_ExportCsvButton;
        ToolbarToggle m_FilterKernelCode;
        int m_CurrentThread;
        bool m_HasData;
        readonly List<VisualElement> m_ToolbarItems = new List<VisualElement>();
        readonly MultiColumnHeader m_ColumnHeader;

        public ScrewItHeatMapView()
        {
            Root = new VisualElement { style = { flexGrow = 1 } };

            m_ThreadSelection = new ToolbarMenu();
            m_ThreadSelection.text = "Select Thread";
            m_ToolbarItems.Add(m_ThreadSelection);

            m_FilterKernelCode = new ToolbarToggle
            {
                text = "Reattribute Kernel Samples",
                tooltip = "When active, all samples that are not from unity.exe or from a managed module will be attributed to the first function up the callstack that is from unity.exe or managed."
            };
            m_FilterKernelCode.RegisterValueChangedCallback(_ => RefreshHeatMap());
            m_ToolbarItems.Add(m_FilterKernelCode);

            m_ExportCsvButton = new ToolbarButton
            {
                text = "Export CSV",
                style = { unityTextAlign = TextAnchor.MiddleLeft },
            };
            m_ExportCsvButton.SetEnabled(false);
            m_ExportCsvButton.clicked += ExportCsvButton;
            m_ToolbarItems.Add(m_ExportCsvButton);

            m_ColumnHeader = new MultiColumnHeader(CreateHeaderState())
            {
                canSort = false
            };
            m_HeatMapTreeView = new HeatMapTreeView(new TreeViewState(), m_ColumnHeader);
            var treeContainer = new IMGUIContainer { style = { flexGrow = 1 } };
            treeContainer.onGUIHandler = () => m_HeatMapTreeView.OnGUI(treeContainer.contentRect);
            m_ColumnHeader.ResizeToFit();
            Root.Add(treeContainer);
        }

        public string Name => "Function HeatMap";

        public IEnumerable<VisualElement> ToolbarItems => m_ToolbarItems;

        public void OnEnable()
        {
            m_ColumnHeader.ResizeToFit();
        }
        public void OnDisable() { }

        void ExportCsvButton()
        {
            var result = EditorUtility.SaveFilePanel("Select save file", Application.dataPath, "heatmap", "csv");
            if (string.IsNullOrEmpty(result))
                return;
            var dir = Path.GetDirectoryName(result);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            using (var fs = new StreamWriter(File.OpenWrite(result)))
            {
                fs.WriteLine("Function;NumSamples;Module;IsManaged");
                foreach (var entry in m_HeatMap.SamplesPerFunction)
                {
                    if (entry.Function == -1)
                    {
                        fs.WriteLine($"???;{entry.Samples};???;False");
                        continue;
                    }

                    var funcName = m_Trace.Functions[entry.Function].Name.ToString();
                    var module = m_Trace.Functions[entry.Function].Module;
                    if (module == -1)
                    {
                        fs.WriteLine($"{funcName};{entry.Samples};???;False");
                        continue;
                    }
                    var moduleName = m_Trace.Modules[module].FilePath.ToString();
                    var managed = m_Trace.Modules[module].IsMono;
                    fs.WriteLine($"{funcName};{entry.Samples};{moduleName};{managed}");
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
                if (thread.ThreadName.LengthInBytes == 0)
                    threadName = "Thread " + i + " (unnamed)";
                else
                    threadName = thread.ThreadName.ToString();
                int index = i;
                m_ThreadSelection.menu.AppendAction(threadName, action =>
                {
                    m_ThreadSelection.text = action.name;
                    m_CurrentThread = index;
                    RefreshHeatMap();
                });
            }

            m_ThreadSelection.text = "Select thread";
        }

        public void ClearData()
        {
            if (m_HasData)
            {
                m_HasData = false;
                m_HeatMap.Dispose();
                m_Trace = default;
                m_ExportCsvButton.SetEnabled(false);
                m_ThreadSelection.SetEnabled(false);
                m_HeatMapTreeView.Reload();
            }
        }

        public VisualElement Root { get; }

        [BurstCompile]
        struct HeatMapJob : IJob
        {
            public NativeList<FunctionHeatMap.Entry> HeatMap;
            [ReadOnly]
            public NativeArray<SampleData> Samples;
            public int ThreadIndex;

            public void Execute()
            {
                FunctionHeatMap.BuildFromTrace(HeatMap, Samples, ThreadIndex);
            }
        }

        void UpdateHeatMap(int threadIndex, bool filterKernelModules)
        {
            if (m_HasData)
                m_HeatMap.Dispose();

            NativeArray<SampleData> samples;
            if (filterKernelModules)
            {
                samples = new NativeArray<SampleData>(m_Trace.Samples, Allocator.TempJob);
                new FilterKernelModulesJob
                {
                    Functions = m_Trace.Functions,
                    Modules = m_Trace.Modules,
                    Samples = samples,
                    StackFrames = m_Trace.StackFrames
                }.Run();
            }
            else
                samples = m_Trace.Samples;

            using (var tmpHeatMap = new NativeList<FunctionHeatMap.Entry>(Allocator.TempJob))
            {
                var job = new HeatMapJob
                {
                    Samples = samples,
                    ThreadIndex = threadIndex,
                    HeatMap = tmpHeatMap
                };
                job.Run();
                m_HeatMap.SamplesPerFunction = tmpHeatMap.ToArray(Allocator.Persistent);
            }

            if (filterKernelModules)
                samples.Dispose();

            m_HasData = true;
            m_ExportCsvButton.SetEnabled(true);
            m_HeatMapTreeView.SetData(ref m_Trace, ref m_HeatMap);
        }


        void RefreshHeatMap()
        {
            if (m_CurrentThread < 0)
                return;
            UpdateHeatMap(m_CurrentThread, m_FilterKernelCode.value);
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
            new MultiColumnHeaderState.Column{
                headerContent = new GUIContent("Address"),
                width = 140,
                minWidth = 140,
                maxWidth = 140,
            },
            new MultiColumnHeaderState.Column
            {
                headerContent = new GUIContent("Total samples"),
                width = 100,
                minWidth = 100,
                maxWidth = 100,
            }
        });
    }
}
