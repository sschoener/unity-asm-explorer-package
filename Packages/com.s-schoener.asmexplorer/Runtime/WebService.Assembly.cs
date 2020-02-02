using System;
using System.Collections.Generic;
using System.Reflection;

namespace AsmExplorer
{
    public partial class WebService {
        private void InspectAssembly(HtmlWriter writer, string assemblyName) {
            var asm = _explorer.FindAssembly(assemblyName);
            if (asm == null) {
                writer.Write("Unknown assembly name " + assemblyName);
                return;
            }
            InspectAssembly(writer, asm);
        }

        private void InspectAssembly(HtmlWriter writer, Assembly asm) {
            using (writer.Tag("small")) {
                writer.AHref("Domain", _completePrefix);
            }
            writer.Inline("h2", "Assembly: " + asm.Name);
            writer.Inline("h4", "Namespaces");
            MakeTable(
                writer,
                asm.Namespaces,
                n => NamespaceLink(writer, n, n.RelativeName)
            );
            var unnamed = asm.FindNamespace("");
            if (unnamed != null) {
                writer.Inline("h4", "Types");
                InspectNamespace(writer, unnamed);
            }
        }
    }
}