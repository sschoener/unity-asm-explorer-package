using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace AsmExplorer
{
    public partial class WebService
    {
        void InspectDomain(HtmlWriter writer)
        {
            writer.Inline("h5", AppDomain.CurrentDomain.FriendlyName);
            var assemblies = m_Explorer.Assemblies.ToArray();

            Array.Sort(assemblies, (lhs, rhs) => lhs.FullName.CompareTo(rhs.FullName));
            if (assemblies.Length > 0)
            {
                using (writer.ContainerFluid())
                using (writer.Tag("code"))
                {
                    writer.Inline("h6", "// assemblies");
                    MakeCodeList(
                        writer,
                        assemblies,
                        a => AssemblyLink(writer, a),
                        a => writer.Write("    // " + a.FullName)
                    );
                }
            }
        }
    }
}
