using System;
using System.Collections.Generic;
using System.Reflection;

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

        private void InspectNamespace(HtmlWriter writer, Namespace ns) {
            using (writer.Tag("small")) {
                AssemblyLink(writer, ns.Assembly);
            }
            writer.Inline("h2", ns.FullName);
            MakeTable(
                writer,
                ns.Types,
                t => WriteShortTypeDeclaration(writer, t)
            );
        }

        private void WriteShortTypeDeclaration(HtmlWriter writer, Type type) {
            if (type.IsEnum) {
                writer.Write("enum ");
            } else if (type.IsInterface) {
                writer.Write("interface ");
            } else if (type.IsValueType) {
                writer.Write("struct ");
            } else if (type.IsClass) {
                writer.Write("class ");
            }
            TypeLink(writer, type, type.Name);
        }
    }
}