using System;
using System.Linq;

namespace AsmExplorer
{
    public partial class WebService
    {
        private void InspectNamespace(HtmlWriter writer, string assemblyName, string namespaceName)
        {
            var asm = _explorer.FindAssembly(assemblyName);
            if (asm == null)
            {
                writer.Write("Unknown assembly name " + assemblyName);
                return;
            }

            var ns = asm.FindNamespace(namespaceName);
            if (ns == null)
            {
                writer.Write("Unknown namespace name " + namespaceName);
                return;
            }

            InspectNamespace(writer, ns);
        }

        void WriteNamespaceMembers(HtmlWriter writer, Namespace ns)
        {
            using (writer.Tag("code"))
            {
                foreach (var group in ns.Types.Where(t => t.DeclaringType == null).GroupBy(t => TypeKinds.Classify(t)).OrderBy(group => group.Key))
                {
                    using (writer.ContainerFluid())
                    {
                        writer.Inline("h6", "// " + group.Key.KindName());
                        MakeCodeList(
                            writer,
                            group.OrderBy(t => t.Name),
                            t => WriteInlineAttributes(writer, t.GetCustomAttributes(false)),
                            t => WriteShortTypeDeclaration(writer, t)
                        );
                    }
                }
            }
        }

        private void InspectNamespace(HtmlWriter writer, Namespace ns)
        {
            using (writer.Tag("small"))
            {
                DomainLink(writer);
                writer.Write(" | ");
                AssemblyLink(writer, ns.Assembly);
            }

            using (writer.ContainerFluid())
            {
                using (writer.Tag("code"))
                using (writer.Tag("h5"))
                {
                    writer.Write("namespace ");
                    NamespaceLink(writer, ns, ns.PrettyName);
                }

                WriteNamespaceMembers(writer, ns);
            }
        }

        private void WriteShortTypeDeclaration(HtmlWriter writer, Type type)
        {
            writer.Write(type.GetAccessModifier().Pretty());
            writer.Write(" ");
            if (type.IsEnum)
            {
                writer.Write("enum ");
            }
            else if (type.IsInterface)
            {
                writer.Write("interface ");
            }
            else if (type.IsValueType)
            {
                writer.Write("struct ");
            }
            else if (type.IsClass)
            {
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
