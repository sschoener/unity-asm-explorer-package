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
                DomainLink(writer);
            }
            writer.Inline("h5", asm.Name);

            using (writer.ContainerFluid())
            {
                var namespaces = asm.Namespaces.Where(s => s.Name.Length > 0).ToList();
                if (namespaces.Count > 0)
                {
                    using (writer.ContainerFluid())
                    using (writer.Tag("code"))
                    {
                        writer.Inline("h6", "// namespaces");
                        MakeCodeList(
                            writer,
                            namespaces.OrderBy(n => n.Name),
                            n =>
                            {
                                writer.Write("namespace ");
                                NamespaceLink(writer, n, n.RelativeName);
                            });
                    }
                }

                var unnamed = asm.FindNamespace("");
                if (unnamed != null)
                {
                    using (writer.ContainerFluid())
                    using (writer.Tag("code"))
                    {
                        writer.Inline("h6", "// Root namespace");
                        WriteNamespaceMembers(writer, unnamed);
                    }
                }
            }
        }
    }
}