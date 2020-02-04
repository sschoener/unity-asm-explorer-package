using System;
using System.Linq;

namespace AsmExplorer
{
    public partial class WebService {
        private void InspectNamespace(HtmlWriter writer, string assemblyName, string namespaceName) {
            var asm = _explorer.FindAssembly(assemblyName);
            if (asm == null) {
                writer.Write("Unknown assembly name " + assemblyName);
                return;
            }
            var ns = asm.FindNamespace(namespaceName);
            if (asm == null) {
                writer.Write("Unknown namespace name " + namespaceName);
                return;
            }
            InspectNamespace(writer, ns);
        }

        enum TypeKind {

        }

        private void InspectNamespace(HtmlWriter writer, Namespace ns) {
            using (writer.Tag("small")) {
                AssemblyLink(writer, ns.Assembly);
            }
            writer.Inline("h2", ns.FullName);
            

            foreach (var group in ns.Types.GroupBy(t => TypeKinds.Classify(t)).OrderBy(group => group.Key)) {
                writer.Inline("h4", group.Key.KindName());
                MakeTable(
                    writer,
                    group.OrderBy(t => t.Name),
                    t => WriteShortTypeDeclaration(writer, t)
                );
            }
        }

        private void WriteShortTypeDeclaration(HtmlWriter writer, Type type) {
            writer.Write(type.GetAccessModifier().Pretty());
            writer.Write(" ");
            if (type.IsEnum) {
                writer.Write("enum ");
            } else if (type.IsInterface) {
                writer.Write("interface ");
            } else if (type.IsValueType) {
                writer.Write("struct ");
            } else if (type.IsClass) {
                if (type.IsAbstract && type.IsSealed)
                    writer.Write("static ");
                else if (type.IsAbstract)
                    writer.Write("abstract ");
                else if (type.IsSealed)
                    writer.Write("sealed ");
                writer.Write("class ");
            }
            TypeLink(writer, type, type.Name);
        }
    }
}
