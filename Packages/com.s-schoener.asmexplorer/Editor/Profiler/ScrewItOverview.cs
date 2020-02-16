using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace AsmExplorer.Profiler
{
    class ScrewItOverview : IScrewItView
    {
        readonly Label m_Info;
        ProfilerTrace m_Trace;
        bool m_HasData;
        readonly List<VisualElement> m_ToolbarItems = new List<VisualElement>();
        public ScrewItOverview()
        {
            Root = new VisualElement { style = { flexGrow = 1 } };
            m_Info = new Label() { style = { flexGrow = 1 } };

            m_ToolbarItems.Add(new ToolbarButton(DumpUnknownSamples)
            {
                text = "Dump unknown samples"
            });
            Root.Add(m_Info);
        }

        void DumpUnknownSamples()
        {
            var modules = m_Trace.Modules.ToList();
            modules.Sort( (lhs, rhs) => lhs.ImageBase.CompareTo(rhs.ImageBase));

            using (var writer = new StreamWriter(File.OpenWrite("unknown_samples.csv")))
            {
                writer.WriteLine("address,module,stacktrace");
                for (int i = 0, n = m_Trace.Samples.Length; i < n; i++)
                {
                    if (m_Trace.Samples[i].Function == -1)
                    {
                        writer.Write(m_Trace.Samples[i].Address.ToString("X16"));
                        writer.Write(',');
                        int module = FindModule(m_Trace.Samples[i].Address);
                        if (module > -1)
                            writer.Write(Path.GetFileName(modules[module].FilePath.ToString()));

                        writer.Write(',');
                        FindCaller(m_Trace.Samples[i].StackTrace, writer);
                        writer.WriteLine();
                    }
                }
            }

            void FindCaller(int stackTrace, StreamWriter writer)
            {
                int idx = stackTrace;
                while (idx > -1)
                {
                    int func = m_Trace.StackFrames[idx].Function;
                    if (func > -1) {
                        var mod = m_Trace.Functions[func].Module;
                        if (mod > -1) {
                            var modName = Path.GetFileName(m_Trace.Modules[mod].FilePath.ToString());
                            writer.Write(modName + "!");
                        }
                        writer.Write(m_Trace.Functions[func].Name + " <- ");
                    }
                    else
                        writer.Write("??? <- ");
                    idx = m_Trace.StackFrames[idx].CallerStackFrame;
                }
            }

            int FindModule(long address)
            {
                ulong addr = (ulong)address;
                for (int i = 0; i < modules.Count; i++)
                {
                    if (addr >= modules[i].ImageBase && addr <= modules[i].ImageEnd)
                        return i;
                }
                return -1;
            }
        }

        public VisualElement Root { get; }
        public IEnumerable<VisualElement> ToolbarItems => m_ToolbarItems;
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
