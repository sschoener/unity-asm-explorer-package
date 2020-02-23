using System;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AsmExplorer.Profiler {

    class ScrewItProfiler : EditorWindow
    {
        ProfilerTrace m_Trace;
        bool m_HasData;
        ToolbarToggle m_RecordingToggle;
        ToolbarMenu m_ViewSelection;
        VisualElement m_CenterElement;
        Toolbar m_Toolbar;

        IScrewItView m_ActiveView;
        ScrewItHeatMapView m_HeatMapView;
        ScrewItOverview m_Overview;
        ScrewItTopDownView m_TopDownView;
        List<IScrewItView> m_Views;

        void OnEnable()
        {
            titleContent = new GUIContent("Screw It! Profiler");
            m_HeatMapView = new ScrewItHeatMapView();
            m_Overview = new ScrewItOverview();
            m_TopDownView = new ScrewItTopDownView();

            rootVisualElement.style.flexDirection = FlexDirection.Column;
            m_Views = new List<IScrewItView> { m_Overview, m_HeatMapView, m_TopDownView };

            {
                m_Toolbar = new Toolbar { style = { height = EditorGUIUtility.singleLineHeight } };
                m_RecordingToggle = new ToolbarToggle();
                m_RecordingToggle.text = "Start Recording";
                m_RecordingToggle.RegisterValueChangedCallback(OnRecordingToggled);
                m_Toolbar.Add(m_RecordingToggle);

                var loadButton = new ToolbarButton { style =  { unityTextAlign = TextAnchor.MiddleLeft } };
                loadButton.clicked += ShowLoadTraceFileDialog;
                loadButton.text = "Load Trace";
                m_Toolbar.Add(loadButton);

                m_Toolbar.Add(new ToolbarSpacer { style = { flexGrow = 1 } });

                m_ViewSelection =  new ToolbarMenu
                {
                    variant = ToolbarMenu.Variant.Popup,
                    text = "Select view"
                };
                foreach (var view in m_Views)
                {
                    var thisView = view;
                    m_ViewSelection.menu.AppendAction(view.Name, action =>
                    {
                        SetActiveView(thisView);
                    });
                }
                m_ViewSelection.SetEnabled(false);

                m_Toolbar.Add(m_ViewSelection);

                rootVisualElement.Add(m_Toolbar);
            }

            {
                // setup center
                m_CenterElement = new VisualElement { style = { flexGrow = 1 } };;
                rootVisualElement.Add(m_CenterElement);
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

        void SetActiveView(IScrewItView view)
        {
            if (m_ActiveView != null)
            {
                m_ActiveView.OnDisable();
                m_CenterElement.Remove(m_ActiveView.Root);
                foreach (var elem in m_ActiveView.ToolbarItems)
                    m_Toolbar.Remove(elem);
            }

            m_ActiveView = view;

            if (m_ActiveView != null)
            {
                m_CenterElement.Add(m_ActiveView.Root);
                m_ActiveView.OnEnable();
                m_ViewSelection.text = m_ActiveView.Name;
                foreach (var elem in m_ActiveView.ToolbarItems)
                    m_Toolbar.Insert(m_Toolbar.childCount - 1, elem);
            }
            else
                m_ViewSelection.text = "Select view";
        }

        static string DefaultTracePath
        {
            get
            {
                var path = Path.Combine(ScrewItConfig.BasePath, "Traces");
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);
                return path;
            }
        }
        const string k_TracePathPref = "ScrewIt.TracePath";
        static string LastTraceFolder
        {
            get => EditorPrefs.GetString(k_TracePathPref, DefaultTracePath);
            set => EditorPrefs.SetString(k_TracePathPref, value);
        }

        void OnRecordingToggled(ChangeEvent<bool> evt)
        {
            if (ProfilerSessionInstance.SessionActive)
            {
                var file = ProfilerSessionInstance.StopSession();
                if (!string.IsNullOrEmpty(file))
                    LoadTraceFile(file);
            }
            else
            {
                string file = EditorUtility.SaveFilePanel("Select trace path", LastTraceFolder, "ProfileTrace", "ptrace");
                if (string.IsNullOrEmpty(file))
                    return;
                var dir = Path.GetDirectoryName(file);
                if (Directory.Exists(dir))
                    LastTraceFolder = dir;
                ProfilerSessionInstance.SetupSession(file);
            }

            bool isActive = ProfilerSessionInstance.SessionActive;
            m_RecordingToggle.SetValueWithoutNotify(isActive);
            m_RecordingToggle.text = isActive ? "Stop Recording" : "Start Recording";
        }

        static readonly string[] k_ProfileFileFilter = { "Profiler Traces", "ptrace" };
        void ShowLoadTraceFileDialog()
        {
            string file = EditorUtility.OpenFilePanelWithFilters("Select trace", LastTraceFolder, k_ProfileFileFilter);
            if (string.IsNullOrEmpty(file) || !File.Exists(file))
                return;
            LastTraceFolder = Path.GetDirectoryName(file);
            LoadTraceFile(file);
        }

        void LoadTraceFile(string path)
        {
            ClearData();
            using (var stream = File.OpenRead(path))
            {
                ProfilerDataSerialization.ReadProfilerTrace(ref m_Trace, stream, Allocator.Persistent);
            }

            foreach (var view in m_Views)
                view.SetData(ref m_Trace);
            m_HasData = true;
            SetActiveView(m_Overview);
            m_ViewSelection.SetEnabled(true);
        }

        void ClearData()
        {
            if (m_HasData)
            {
                foreach (var view in m_Views)
                    view.ClearData();
                m_Trace.Dispose();
                m_HasData = false;
                m_ViewSelection.SetEnabled(false);
                SetActiveView(null);
            }
        }

        void OnDisable()
        {
            ClearData();
        }
    }

    interface IScrewItView
    {
        string Name { get;  }
        VisualElement Root { get; }
        IEnumerable<VisualElement> ToolbarItems { get; }
        void OnEnable();
        void OnDisable();
        void SetData(ref ProfilerTrace trace);
        void ClearData();
    }
}
