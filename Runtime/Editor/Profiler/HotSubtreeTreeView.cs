using System;
using System.Collections.Generic;
using System.IO;
using AsmExplorer.Profiler;
using Unity.Collections;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace AsmExplorer
{
    class HotSubtreeTreeView : TreeView
    {
        ProfilerTrace m_Trace;
        NativeArray<FunctionSampleData> m_FunctionSamples;
        string[] m_Name;
        string[] m_NumSamplesSelf;
        string[] m_NumSamplesTotal;
        string[] m_Module;
        string[] m_Addresses;
        GUIStyle m_RightAlignedLabelStyle;

        public HotSubtreeTreeView(TreeViewState state, MultiColumnHeader multiColumnHeader)
            : base(state, multiColumnHeader)
        {
            showAlternatingRowBackgrounds = true;
        }

        public void SetData(ref ProfilerTrace trace, NativeArray<FunctionSampleData> functionSamples)
        {
            m_Trace = trace;
            m_FunctionSamples = functionSamples;
            m_Name = new string[trace.Functions.Length];
            m_NumSamplesSelf = new string[trace.Functions.Length];
            m_NumSamplesTotal = new string[trace.Functions.Length];
            m_Module = new string[trace.Functions.Length];
            m_Addresses = new string[trace.Functions.Length];
            Reload();
        }

        public void ClearData()
        {
            m_FunctionSamples = default;
            m_Trace = default;
        }

        protected override TreeViewItem BuildRoot()
        {
            var root = new TreeViewItem(0)
            {
                depth = -1,
                id = 0
            };
            root.children = new List<TreeViewItem>(m_FunctionSamples.Length);
            for (int i = 0, n = m_FunctionSamples.Length; i < n; i++)
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

        enum Column
        {
            FunctionName = 0,
            ModuleName,
            Address,
            SamplesSelf,
            SamplesTotal
        }

        protected override void RowGUI(RowGUIArgs args)
        {
            if (m_RightAlignedLabelStyle == null)
            {
                m_RightAlignedLabelStyle = new GUIStyle(GUI.skin.label);
                m_RightAlignedLabelStyle.alignment = TextAnchor.MiddleRight;
            }

            for (int i = 0, n = args.GetNumVisibleColumns(); i < n; i++)
            {
                var rect = args.GetCellRect(i);
                var col = (Column)args.GetColumn(i);
                int idx = args.item.id - 1;
                switch (col)
                {
                    case Column.FunctionName:
                    {
                        if (m_Name[idx] == null)
                        {
                            var funcIndex = m_FunctionSamples[idx].Function;
                            if (funcIndex < 0)
                                m_Name[idx] = "???";
                            else
                                m_Name[idx] = m_Trace.Functions[funcIndex].Name.ToString();
                        }

                        EditorGUI.LabelField(rect, m_Name[idx]);
                        break;
                    }
                    case Column.ModuleName:
                    {
                        if (m_Module[idx] == null)
                        {
                            var funcIndex = m_FunctionSamples[idx].Function;
                            if (funcIndex < 0)
                                m_Module[idx] = "???";
                            else
                            {
                                int moduleIndex = m_Trace.Functions[funcIndex].Module;
                                if (moduleIndex < 0)
                                    m_Module[idx] = "???";
                                else
                                {
                                    var module = m_Trace.Modules[moduleIndex];
                                    string name = Path.GetFileName(module.FilePath.ToString());
                                    if (module.IsMono)
                                        name += " (managed)";
                                    m_Module[idx] = name;
                                }
                            }
                        }

                        EditorGUI.LabelField(rect, m_Module[idx]);
                        break;
                    }
                    case Column.Address:
                    {
                        if (m_Addresses[idx] == null)
                        {
                            var funcIndex = m_FunctionSamples[idx].Function;
                            m_Addresses[idx] = funcIndex < 0 ? "???" : m_Trace.Functions[funcIndex].BaseAddress.ToString("X16");
                        }

                        EditorGUI.LabelField(rect, m_Addresses[idx]);
                        break;
                    }
                    case Column.SamplesSelf:
                    {
                        if (m_NumSamplesSelf[idx] == null)
                            m_NumSamplesSelf[idx] = m_FunctionSamples[idx].Self.ToString();
                        EditorGUI.LabelField(rect, m_NumSamplesSelf[idx], m_RightAlignedLabelStyle);
                        break;
                    }
                    case Column.SamplesTotal:
                    {
                        if (m_NumSamplesTotal[idx] == null)
                            m_NumSamplesTotal[idx] = m_FunctionSamples[idx].Total.ToString();
                        EditorGUI.LabelField(rect, m_NumSamplesTotal[idx], m_RightAlignedLabelStyle);
                        break;
                    }
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }
    }
}
