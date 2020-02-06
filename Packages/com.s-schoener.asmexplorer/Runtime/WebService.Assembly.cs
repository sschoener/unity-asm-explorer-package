using System.Linq;

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
            var namespaces = asm.Namespaces.Where(s => s.Name.Length > 0).ToList();
            if (namespaces.Count > 0)
            {
                writer.Inline("h4", "Namespaces");
                MakeTable(
                    writer,
                    namespaces.OrderBy(n => n.Name),
                    n => NamespaceLink(writer, n, n.RelativeName)
                );
            }

            var unnamed = asm.FindNamespace("");
            if (unnamed != null) {
                writer.Inline("h4", "Root namespace");
                InspectNamespace(writer, unnamed);
            }
        }
    }
}