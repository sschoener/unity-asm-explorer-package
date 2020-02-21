using System.Collections.Generic;
using System.IO;
using AsmExplorer.Profiler;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace AsmExplorer
{
    class TopDownTreeView : TreeView
    {
        ProfilerTrace m_Trace;
        TopDownTreeData m_TreeData;

        string[] m_Name;
        string[] m_TotalSamples;
        string[] m_Addresses;
        string[] m_SelfSamples;
        string[] m_Module;
        GUIStyle m_RightAlignedLabelStyle;
        readonly List<TreeViewItem> m_RowsCache = new List<TreeViewItem>(100);

        public TopDownTreeView(TreeViewState state, MultiColumnHeader multiColumnHeader)
            : base(state, multiColumnHeader)
        {
            showAlternatingRowBackgrounds = true;
        }

        public void SetData(ref ProfilerTrace trace, TopDownTreeData treeData)
        {
            m_Trace = trace;
            m_TreeData = treeData;
            int n = treeData.Frames.Length;
            if (m_Name?.Length == n) {
                System.Array.Clear(m_Name, 0, m_Name.Length);
                System.Array.Clear(m_TotalSamples, 0, m_TotalSamples.Length);
                System.Array.Clear(m_SelfSamples, 0, m_SelfSamples.Length);
                System.Array.Clear(m_Module, 0, m_Module.Length);
                System.Array.Clear(m_Addresses, 0, m_Addresses.Length);
            } else {
                m_Name = new string[n];
                m_TotalSamples = new string[n];
                m_SelfSamples = new string[n];
                m_Module = new string[n];
                m_Addresses = new string[n];
            }
            Reload();
        }

        public void ClearData() {
            m_Trace = default;
            m_TreeData = default;
        }

        protected override TreeViewItem BuildRoot()
        {
            return new TreeViewItem
            {
                depth = -1,
                id = 0
            };
        }

        protected override IList<TreeViewItem> BuildRows(TreeViewItem root)
        {
            m_RowsCache.Clear();
            if (!m_TreeData.Frames.IsCreated)
                return m_RowsCache;
            var childToData = m_TreeData.Tree.ChildToDataIndex;
            var stack = new Stack<(int index, int depth)>();
            {
                var rootChildren = m_TreeData.Tree.Root;
                for (int i = rootChildren.Count - 1; i >= 0; i--) {
                    stack.Push((rootChildren.Offset + i, 0));
                }
            }
            while (stack.Count > 0) {
                (var idx, var depth) = stack.Pop();
                int dataItem = childToData[idx];
                var item = new TreeViewItem {
                    id = dataItem,
                    depth = depth
                };
                
                m_RowsCache.Add(item);
                var children = m_TreeData.Tree.LookUpChildren(dataItem);
                if (!IsExpanded(dataItem)) {
                    if (children.Count > 0)
                        item.children = CreateChildListForCollapsedParent();
                    continue;
                }                
                for (int i = children.Count - 1; i >= 0; i--)
                    stack.Push((children.Offset + i, depth + 1));
            }
            SetupParentsAndChildrenFromDepths(root, m_RowsCache);

            return m_RowsCache;
        }

        protected override IList<int> GetAncestors (int id)
		{
			throw new System.NotImplementedException(nameof(GetAncestors));
		}

		protected unsafe override IList<int> GetDescendantsThatHaveChildren (int id)
		{
            throw new System.NotImplementedException(nameof(GetDescendantsThatHaveChildren));
		}

        enum Column
        {
            FunctionName = 0,
            ModuleName,
            Address,
            TotalSamples,
            SelfSamples,
        }

        void InitStyles() {
            if (m_RightAlignedLabelStyle != null)
                return;
            m_RightAlignedLabelStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleRight
            };
        }

        protected override void RowGUI(RowGUIArgs args)
        {
            InitStyles();

            var idx = args.item.id;
            for (int i = 0, n = args.GetNumVisibleColumns(); i < n; i++)
            {
                var rect = args.GetCellRect(i);
                var col = (Column)args.GetColumn(i);
                switch (col)
                {
                    case Column.FunctionName:
                        {
                            if (m_Name[idx] == null)
                            {
                                var funcIndex = m_TreeData.Frames[idx].FrameData.Function;
                                if (funcIndex < 0)
                                    m_Name[idx] = "???";
                                else
                                    m_Name[idx] = m_Trace.Functions[funcIndex].Name.ToString();
                            }
                            float indent = GetContentIndent(args.item);
                            rect.x += indent;
                            rect.width -= indent;
                            EditorGUI.LabelField(rect, m_Name[idx]);
                            break;
                        }
                    case Column.ModuleName:
                        {
                            if (m_Module[idx] == null)
                            {
                                var funcIndex = m_TreeData.Frames[idx].FrameData.Function;
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
                            if (m_Addresses[idx] == null) {
                                m_Addresses[idx] = m_TreeData.Frames[idx].FrameData.Address.ToString("X16");
                            }
                            EditorGUI.LabelField(rect, m_Addresses[idx]);
                            break;
                        }
                    case Column.TotalSamples:
                        {
                            if (m_TotalSamples[idx] == null)
                                m_TotalSamples[idx] = m_TreeData.Frames[idx].NumSamplesTotal.ToString();

                            EditorGUI.LabelField(rect, m_TotalSamples[idx], m_RightAlignedLabelStyle);
                            break;
                        }
                    case Column.SelfSamples:
                        {
                            if (m_SelfSamples[idx] == null)
                                m_SelfSamples[idx] = m_TreeData.Frames[idx].NumSamplesSelf.ToString();

                            EditorGUI.LabelField(rect, m_SelfSamples[idx], m_RightAlignedLabelStyle);
                            break;
                        }
                    default:
                        throw new System.ArgumentOutOfRangeException(nameof(col), col.ToString());
                }
            }
        }
    }
}
