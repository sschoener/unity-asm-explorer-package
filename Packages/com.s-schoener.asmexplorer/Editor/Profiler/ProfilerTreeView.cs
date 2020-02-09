using System;
using System.Collections.Generic;
using System.IO;
using AsmExplorer.Profiler;
using Microsoft.Diagnostics.Tracing.Parsers.AspNet;
using Unity.Collections.LowLevel.Unsafe;
using UnityEditor;
using UnityEditor.IMGUI.Controls;

namespace AsmExplorer {
    class ProfilerTreeView : TreeView
    {
        FunctionHeatMap m_HeatMap;
        ProfilerTrace m_Trace;
        string[] m_Name;
        string[] m_NumSamples;
        string[] m_Module;

        public ProfilerTreeView(TreeViewState state, MultiColumnHeader multiColumnHeader)
            : base(state, multiColumnHeader) { }

        public void SetData(ProfilerTrace trace, FunctionHeatMap heatMap)
        {
            m_Trace = trace;
            m_HeatMap = heatMap;
            m_Name = new string[trace.Functions.Length];
            m_NumSamples = new string[trace.Functions.Length];
            m_Module = new string[trace.Functions.Length];
        }

        protected override TreeViewItem BuildRoot()
        {
            var root = new TreeViewItem(0)
            {
                depth = -1,
                id = 0
            };
            root.children = new List<TreeViewItem>(m_HeatMap.SamplesPerFunction.Length);
            for (int i = 0, n = m_HeatMap.SamplesPerFunction.Length; i < n; i++)
            {
                root.children.Add(
                    new TreeViewItem
                    {
                        depth = 0,
                        id = i + 1,
                        parent = root
                    }
                );
            }
            return root;
        }

        protected override void RowGUI(RowGUIArgs args)
        {
            for (int i = 0, n = args.GetNumVisibleColumns(); i < n; i++)
            {
                var rect = args.GetCellRect(i);
                var col = args.GetColumn(i);
                int idx = args.item.id - 1;
                if (col == 0)
                {
                    if (m_Name[idx] == null)
                    {
                        var funcIndex = m_HeatMap.SamplesPerFunction[idx].Function;
                        if (funcIndex < 0)
                            m_Name[idx] = "???";
                        else
                            m_Name[idx] = m_Trace.Functions[funcIndex].Name.ToString();
                    }
                    EditorGUI.LabelField(rect, m_Name[idx]);
                } else if (col == 1)
                {
                    if (m_NumSamples[idx] == null)
                        m_NumSamples[idx] = m_HeatMap.SamplesPerFunction[idx].Samples.ToString();
                    EditorGUI.LabelField(rect, m_NumSamples[idx]);
                } else if (col == 2)
                {
                    if (m_Module[idx] == null)
                    {
                        var funcIndex = m_HeatMap.SamplesPerFunction[idx].Function;
                        if (funcIndex < 0)
                            m_Module[idx] = "???";
                        else
                        {
                            int module = m_Trace.Functions[funcIndex].Module;
                            if (module < 0)
                                m_Module[idx] = "???";
                            else
                                m_Module[idx] = Path.GetFileName(m_Trace.Modules[module].FilePath.ToString());
                        }
                    }
                    EditorGUI.LabelField(rect, m_Module[idx]);
                }
            }
        }
    }
}
