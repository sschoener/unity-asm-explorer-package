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
    class ScrewItHeatMapView : IScrewItView
    {
        readonly HeatMapTreeView m_HeatMapTree;
        readonly ToolbarMenu m_ThreadSelection;
        FunctionHeatMap m_HeatMap;
        ProfilerTrace m_Trace;
        ToolbarButton m_ExportCsvButton;
        bool m_HasData;
        readonly List<VisualElement> m_ToolbarItems = new List<VisualElement>();

        public ScrewItHeatMapView()
        {
            Root = new VisualElement { style = { flexGrow = 1 } };

            m_ThreadSelection = new ToolbarMenu();
            m_ThreadSelection.text = "Select Thread";
            m_ToolbarItems.Add(m_ThreadSelection);

            m_ExportCsvButton = new ToolbarButton
            {
                text = "Export CSV",
                style = { unityTextAlign = TextAnchor.MiddleLeft }
            };
            m_ExportCsvButton.SetEnabled(false);
            m_ExportCsvButton.clicked += ExportCsvButton;
            m_ToolbarItems.Add(m_ExportCsvButton);

            var mch = new MultiColumnHeader(CreateHeaderState());
            mch.canSort = false;
            m_HeatMapTree = new HeatMapTreeView(new TreeViewState(), mch);
            m_HeatMapTree.Reload();
            var treeContainer = new IMGUIContainer { style = { flexGrow = 1 } };
            treeContainer.onGUIHandler = () => m_HeatMapTree.OnGUI(treeContainer.contentRect);
            mch.ResizeToFit();
            Root.Add(treeContainer);
        }

        public string Name => "Function HeatMap";

        public IEnumerable<VisualElement> ToolbarItems => m_ToolbarItems;
        public void OnEnable() { }
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
                    UpdateHeatMap(index);
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
            }
        }

        public VisualElement Root { get; }

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
            if (m_HasData)
                m_HeatMap.Dispose();
            var tmpHeatMap = new NativeList<FunctionHeatMap.Entry>(Allocator.TempJob);
            var job = new HeatMapJob
            {
                Trace = m_Trace,
                ThreadIndex = threadIndex,
                HeatMap = tmpHeatMap
            };
            job.Run();
            m_HeatMap.SamplesPerFunction = tmpHeatMap.ToArray(Allocator.Persistent);
            m_HasData = true;
            tmpHeatMap.Dispose();

            m_ExportCsvButton.SetEnabled(true);
            m_HeatMapTree.SetData(m_Trace, m_HeatMap);
            m_HeatMapTree.Reload();
        }

        static MultiColumnHeaderState CreateHeaderState() => new MultiColumnHeaderState(new[]
        {
            new MultiColumnHeaderState.Column
            {
                headerContent = new GUIContent("Function name"),
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
