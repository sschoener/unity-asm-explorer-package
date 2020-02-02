using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace AsmExplorer
{
    public partial class WebService {
        private void InspectDomain(HtmlWriter writer) {
            writer.Inline("h2", "Domain: " + AppDomain.CurrentDomain.FriendlyName);
            var assemblies = _explorer.Assemblies.ToArray();
            Array.Sort(assemblies, (lhs, rhs) => lhs.FullName.CompareTo(rhs.FullName));
            MakeTable(
                writer,
                assemblies,
                a => AssemblyLink(writer, a),
                a => writer.Write(a.FullName)
            );
        }
    }
}