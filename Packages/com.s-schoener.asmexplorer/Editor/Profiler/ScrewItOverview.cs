using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;

namespace AsmExplorer.Profiler {
    class ScrewItOverview : IScrewItView
    {
        readonly Label m_Info;
        ProfilerTrace m_Trace;
        bool m_HasData;
        public ScrewItOverview()
        {
            Root = new VisualElement { style = { flexGrow = 1 } };
            m_Info = new Label() { style = { flexGrow = 1 } };
            Root.Add(m_Info);
        }

        public VisualElement Root { get; }
        public IEnumerable<VisualElement> ToolbarItems => Enumerable.Empty<VisualElement>();
        public string Name => "Overview";

        public void OnEnable()
        {
            var startDate = new DateTime().AddTicks(m_Trace.Header.SessionStart);
            var endDate = new DateTime().AddTicks(m_Trace.Header.SessionEnd);
            m_Info.text =
                "Recorded on " + startDate.ToString("F") + '\n' +
                "Duration: " + (endDate - startDate).Seconds.ToString("F0") + "s\n" +
                "Sampling interval: " + m_Trace.Header.SamplingInterval.ToString("F3") + "ms\n" +
                "Number of samples: " + m_Trace.Samples.Length;
        }

        public void OnDisable() { }
        public void SetData(ref ProfilerTrace trace)
        {
            m_HasData = true;
            m_Trace = trace;
        }

        public void ClearData()
        {
            if (m_HasData)
            {
                m_HasData = false;
            }
            m_Trace = default;
        }
    }
}
