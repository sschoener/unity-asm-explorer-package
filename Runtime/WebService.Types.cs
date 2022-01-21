using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine.Experimental.GlobalIllumination;

namespace AsmExplorer
{
    public partial class WebService
    {
        private void InspectType(HtmlWriter writer, string assemblyName, string typeName)
        {
            var asm = m_Explorer.FindAssembly(assemblyName);
            if (asm == null)
            {
                writer.Write("Unknown assembly name " + assemblyName);
                return;
            }

            var type = asm.FindType(typeName);
            if (type == null)
            {
                writer.Write("Unknown type name " + typeName + " in " + asm.FullName);
                return;
            }

            InspectType(writer, asm, type);
        }

        private void InspectType(HtmlWriter writer, Assembly assembly, Type type)
        {
            var asm = assembly;
            var ns = asm.FindNamespace(type.Namespace);
            using (writer.Tag("small"))
            {
                DomainLink(writer);
                writer.Write(" | ");
                AssemblyLink(writer, asm);
                writer.Write(" | ");
                NamespaceLink(writer, ns, type.Namespace ?? "<root>");

                // see whether this is a nested type
                if (type.DeclaringType != null)
                {
                    writer.Write(" | ");
                    TypeLink(writer, type.DeclaringType);
                }
            }

            using (writer.ContainerFluid())
            using (writer.Tag("code"))
            {
                writer.Write("namespace ");
                NamespaceLink(writer, ns, string.IsNullOrEmpty(ns.FullName) ? "<root>" : ns.FullName);

                HtmlWriter.TagHandle outerClass = default;
                if (type.DeclaringType != null)
                {
                    outerClass = writer.ContainerFluid();
                    WriteTypeDeclaration(writer, type.DeclaringType);
                }

                using (writer.ContainerFluid())
                {
                    var attr = type.GetCustomAttributes(false);
                    if (attr.Length > 0)
                    {
                        WriteAttributes(writer, attr);
                    }

                    using (writer.Tag("h5"))
                        WriteTypeDeclaration(writer, type);

                    if (type.IsGenericType && type.GenericTypeArguments.Length != 0)
                    {
                        writer.Break();
                        writer.Write("// instance of generic definition ");
                        TypeLink(writer, type.GetGenericTypeDefinition(), type.Name);
                    }

                    if (type.IsClass || type.IsValueType)
                    {
                        InspectClass(writer, type);
                    }
                    else if (type.IsInterface)
                    {
                        InspectInterface(writer, type);
                    }
                    else if (type.IsEnum)
                    {
                        InspectEnum(writer, type);
                    }
                }
                outerClass.Dispose();
            }
        }

        private void InspectEnum(HtmlWriter writer, Type type)
        {
            var fields = type.GetFields();
            MakeCodeList(
                writer,
                fields.Where(f => f.IsLiteral),
                f => writer.Write(f.Name),
                f =>
                {
                    var value = f.GetValue(null);
                    object underlyingValue = System.Convert.ChangeType(value, type.UnderlyingSystemType);
                    writer.Write(underlyingValue.ToString());
                }
            );
        }

        private void InspectInterface(HtmlWriter writer, Type type)
        {
            LayoutInstanceProperties(writer, type);
            LayoutInstanceMethods(writer, type);
        }

        private void InspectClass(HtmlWriter writer, Type type)
        {
            var instanceCtors = type.GetConstructors(k_AllInstanceBindings);
            Array.Sort(instanceCtors, (lhs, rhs) => ReflectionHelper.CompareConstructors(type, lhs, rhs));
            if (instanceCtors.Length > 0)
            {
                using (writer.ContainerFluid())
                {
                    writer.Inline("h6", "// Constructors");
                    LayoutCtors(writer, instanceCtors);
                }
            }

            var staticCtor = type.GetConstructors(k_AllStaticBindings);
            if (staticCtor.Length > 0)
            {
                Array.Sort(staticCtor, (lhs, rhs) => ReflectionHelper.CompareConstructors(type, lhs, rhs));
                using (writer.ContainerFluid())
                {
                    writer.Inline("h6", "// Static constructors");
                    LayoutCtors(writer, staticCtor);
                }
            }

            LayoutInstanceFields(writer, type);

            var staticFields = type.GetFields(k_AllStaticBindings);
            if (staticFields.Length > 0)
            {
                Array.Sort(staticFields, (lhs, rhs) => ReflectionHelper.CompareFields(type, lhs, rhs));
                using (writer.ContainerFluid())
                {
                    writer.Inline("h6", "// Static fields");
                    LayoutStaticFields(writer, staticFields);
                }
            }

            LayoutInstanceProperties(writer, type);

            var staticProperties = type.GetProperties(k_AllStaticBindings);
            if (staticProperties.Length > 0)
            {
                Array.Sort(staticProperties, (lhs, rhs) => ReflectionHelper.CompareProperties(type, lhs, rhs));
                using (writer.ContainerFluid())
                {
                    writer.Inline("h6", "Static properties");
                    LayoutProperties(writer, staticProperties);
                }
            }

            LayoutInstanceMethods(writer, type);

            var staticMethods = type.GetMethods(k_AllStaticBindings);
            if (staticMethods.Length > 0)
            {
                Array.Sort(staticMethods, (lhs, rhs) => ReflectionHelper.CompareMethods(type, lhs, rhs));
                using (writer.ContainerFluid())
                {
                    writer.Inline("h6", "// Static functions");
                    LayoutMethods(writer, staticMethods.Where(m => !m.IsSpecialName));
                }
            }

            LayoutNestedTypes(writer, type);
        }

        private void WriteFieldModifiers(HtmlWriter writer, FieldInfo f) {
            writer.Write(f.GetAccessModifier().Pretty());
            if (f.IsLiteral)
                writer.Write(" const");
            else if (f.IsStatic)
                writer.Write(" static");
            else if (f.IsInitOnly)
                writer.Write(" readonly");
        }

        private void WriteFieldType(HtmlWriter writer, FieldInfo f) {
            if (f.FieldType.IsByRef)
            {
                writer.Write("ref ");
                WriteTypeName(writer, f.FieldType.GetElementType());
            }
            else
            {
                WriteTypeName(writer, f.FieldType);
            }
        }

        private void LayoutFields(HtmlWriter writer, IEnumerable<FieldInfo> fields)
        {
            MakeCodeList(
                writer,
                fields,
                f => WriteInlineAttributes(writer, f.GetCustomAttributes(false)),
                f => WriteFieldModifiers(writer, f),
                f => WriteFieldType(writer, f),
                f => writer.Write(f.Name)
            );
        }

        private void LayoutStaticFields(HtmlWriter writer, IEnumerable<FieldInfo> fields)
        {
            MakeCodeList(
                writer,
                fields,
                f => WriteInlineAttributes(writer, f.GetCustomAttributes(false)),
                f =>
                {
                    writer.Write(f.GetAccessModifier().Pretty());
                    if (f.IsLiteral)
                        writer.Write(" const");
                    else if (f.IsStatic)
                        writer.Write(" static");
                    else if (f.IsInitOnly)
                        writer.Write(" readonly");
                },
                f => WriteTypeName(writer, f.FieldType),
                f => writer.Write(f.Name),
                f => writer.Write("= " + (f.GetValue(null)?.ToString() ?? "null"))
            );
        }

        private void LayoutNestedTypes(HtmlWriter writer, Type type)
        {
            var nested = type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic);
            if (nested.Length == 0)
                return;
            using (writer.ContainerFluid())
            {
                writer.Inline("h6", "// Nested types");
                MakeCodeList(
                    writer,
                    nested,
                    t => WriteInlineAttributes(writer, t.GetCustomAttributes(false)),
                    t => WriteTypeDeclaration(writer, t)
                );
            }
        }

        private void LayoutInstanceMethods(HtmlWriter writer, Type type)
        {
            var instanceMethods = type.GetMethods(k_AllInstanceBindings);
            if (instanceMethods.Length == 0)
                return;

            Array.Sort(instanceMethods, (lhs, rhs) => ReflectionHelper.CompareMethods(type, lhs, rhs));
            int split = instanceMethods.Length;
            for (int i = 0; i < instanceMethods.Length; i++)
            {
                if (instanceMethods[i].DeclaringType != type)
                {
                    split = i;
                    break;
                }
            }

            if (split > 0)
            {
                using (writer.ContainerFluid())
                {
                    writer.Inline("h6", "// Instance methods");
                    LayoutMethods(writer, ArrayView(instanceMethods, 0, split).Where(m => !m.IsSpecialName));
                }
            }

            if (split < instanceMethods.Length)
            {
                using (writer.ContainerFluid())
                {
                    writer.Inline("h6", "// Inherited methods");
                    LayoutMethods(writer, ArrayView(instanceMethods, split).Where(m => !m.IsSpecialName));
                }
            }
        }

        private void LayoutInstanceProperties(HtmlWriter writer, Type type)
        {
            var instanceProperties = type.GetProperties(k_AllInstanceBindings);
            if (instanceProperties.Length == 0)
                return;

            Array.Sort(instanceProperties, (lhs, rhs) => ReflectionHelper.CompareProperties(type, lhs, rhs));
            int split = instanceProperties.Length;
            for (int i = 0; i < instanceProperties.Length; i++)
            {
                if (instanceProperties[i].DeclaringType != type)
                {
                    split = i;
                    break;
                }
            }

            if (split > 0)
            {
                using (writer.ContainerFluid())
                {
                    writer.Inline("h6", "// Instance properties");
                    LayoutProperties(writer, ArrayView(instanceProperties, 0, split));
                }
            }

            if (split < instanceProperties.Length)
            {
                using (writer.ContainerFluid())
                {
                    writer.Inline("h6", "// Inherited properties");
                    LayoutProperties(writer, ArrayView(instanceProperties, split));
                }
            }
        }

        private void LayoutInstanceFields(HtmlWriter writer, Type type)
        {
            var instanceFields = type.GetFields(k_AllInstanceBindings);
            if (instanceFields.Length == 0)
                return;
            Array.Sort(instanceFields, (lhs, rhs) => ReflectionHelper.CompareFields(type, lhs, rhs));
            int split = instanceFields.Length;
            for (int i = 0; i < instanceFields.Length; i++)
            {
                if (instanceFields[i].DeclaringType != type)
                {
                    split = i;
                    break;
                }
            }

            if (split > 0)
            {
                using (writer.ContainerFluid())
                {
                    writer.Inline("h6", "// Instance fields");
                    LayoutFields(writer, ArrayView(instanceFields, 0, split));
                }
            }

            if (split < instanceFields.Length)
            {
                using (writer.ContainerFluid())
                {
                    writer.Inline("h6", "// Inherited fields");
                    LayoutFields(writer, ArrayView(instanceFields, split));
                }
            }
        }

        private void LayoutProperties(HtmlWriter writer, IEnumerable<PropertyInfo> properties)
        {
            MakeCodeListWithoutSemicolon(
                writer,
                properties,
                p => WriteInlineAttributes(writer, p.GetCustomAttributes(false)),
                p => WriteTypeName(writer, p.PropertyType),
                p =>
                {
                    writer.Write(p.Name);
                    writer.Write(" { ");
                    if (p.CanRead)
                    {
                        var getter = p.GetGetMethod(nonPublic: true);
                        WriteMethodPrefix(writer, getter);
                        writer.Write(" ");
                        MethodLink(writer, getter, "get");
                        writer.Write("; ");
                    }

                    if (p.CanWrite)
                    {
                        var setter = p.GetSetMethod(nonPublic: true);
                        WriteMethodPrefix(writer, setter);
                        writer.Write(" ");
                        MethodLink(writer, setter, "set");
                        writer.Write(";");
                    }

                    writer.Write(" } ");
                }
            );
        }

        private void WriteGenericArguments(HtmlWriter writer, Type[] types, TypeExt.NameMode mode = TypeExt.NameMode.Short, bool noLink=false)
        {
            writer.Write("<");
            for (int i = 0; i < types.Length; i++)
            {
                if (i > 0)
                    writer.Write(", ");
                if (types[i].IsGenericParameter)
                {
                    var gpa = types[i].GenericParameterAttributes;
                    if ((gpa & GenericParameterAttributes.Covariant) != 0)
                        writer.Write("out ");
                    else if ((gpa & GenericParameterAttributes.Contravariant) != 0)
                        writer.Write("in ");
                    writer.Write(types[i].Name);
                }
                else
                {
                    var txt = types[i].PrettyName(mode);
                    if (noLink)
                        writer.Write(txt);
                    else
                        TypeLink(writer, types[i], txt);
                }
            }

            writer.Write(">");
        }

        private void WriteGenericConstraints(HtmlWriter writer, Type generic, bool noLink=false)
        {
            var constraints = generic.GetGenericParameterConstraints();
            if (constraints.Length == 0)
                return;
            writer.Write(" where ");
            writer.Write(generic.Name);
            writer.Write(" : ");
            var gpa = generic.GenericParameterAttributes;
            for (int i = 0; i < constraints.Length; i++)
            {
                if (i > 0) writer.Write(", ");
                WriteTypeName(writer, constraints[i], noLink);
            }

            bool hasPrevious = constraints.Length > 0;
            if ((gpa & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0)
            {
                if (hasPrevious) writer.Write(", ");
                writer.Write("struct");
                hasPrevious = true;
            }

            if ((gpa & GenericParameterAttributes.ReferenceTypeConstraint) != 0)
            {
                if (hasPrevious) writer.Write(", ");
                writer.Write("class");
                hasPrevious = true;
            }

            if ((gpa & GenericParameterAttributes.DefaultConstructorConstraint) != 0)
            {
                if (hasPrevious) writer.Write(", ");
                writer.Write("new()");
                hasPrevious = true;
            }
        }

        private void LayoutMethods(HtmlWriter writer, IEnumerable<MethodInfo> methods)
        {
            MakeCodeList(
                writer,
                methods,
                m => WriteInlineAttributes(writer, m.GetCustomAttributes(false)),
                m => WriteMethodPrefix(writer, m),
                m => WriteMethodReturnType(writer, m),
                m => WriteMethodDeclaration(writer, m)
            );
        }

        private void WriteParameter(HtmlWriter writer, ParameterInfo p, bool noLink=false)
        {
            var pt = p.ParameterType;
            if (p.IsIn)
            {
                writer.Write("in ");
            }

            if (p.IsOut)
            {
                writer.Write("out ");
            }
            else if (p.ParameterType.IsByRef)
            {
                if (!p.IsOut && !p.IsIn)
                    writer.Write("ref ");
                pt = p.ParameterType.GetElementType();
            }

            WriteTypeName(writer, pt, noLink);
            writer.Write(" ");
            writer.Write(p.Name);
        }

        private void LayoutCtors(HtmlWriter writer, IEnumerable<ConstructorInfo> methods)
        {
            MakeCodeList(
                writer,
                methods,
                c => WriteCtorPrefix(writer, c),
                c => WriteCtorDeclaration(writer, c)
            );
        }

        private static Dictionary<Type, string> _specialNames = new Dictionary<Type, string>()
        {
            { typeof(long), "long" },
            { typeof(int), "int" },
            { typeof(short), "short" },
            { typeof(sbyte), "sbyte" },
            { typeof(ulong), "ulong" },
            { typeof(uint), "uint" },
            { typeof(ushort), "ushort" },
            { typeof(byte), "byte" },
            { typeof(void), "void" },
            { typeof(string), "string" },
            { typeof(bool), "bool" },
            { typeof(object), "object" }
        };

        private void WriteTypeName(HtmlWriter writer, Type type, bool noLink=false)
        {
            if (type.IsGenericParameter)
            {
                writer.Write(type.Name);
                return;
            }

            if (_specialNames.TryGetValue(type, out var name))
            {
                if (noLink)
                    writer.Write(name);
                else
                    TypeLink(writer, type, name);
            }
            else if (type.IsGenericType)
            {
                var txt = type.Namespace + "." + type.Name;
                if (noLink)
                    writer.Write(txt);
                else
                    TypeLink(writer, type, txt);
                writer.Write("<");
                var arguments = type.GetGenericArguments();
                for (int i = 0; i < arguments.Length; i++)
                {
                    if (i > 0)
                        writer.Write(", ");
                    WriteTypeName(writer, arguments[i], noLink);
                }

                writer.Write(">");
            }
            else if (type.IsPointer)
            {
                if (noLink)
                    writer.Write("*");
                else
                    TypeLink(writer, type, "*");
                WriteTypeName(writer, type.GetElementType(), noLink);
            }
            else if (type.IsArray)
            {
                WriteTypeName(writer, type.GetElementType(), noLink);
                if (noLink)
                    writer.Write("[]");
                else
                    TypeLink(writer, type, "[]");
            }
            else
            {
                name = type.FullName ?? type.Name;
                if (noLink)
                    writer.Write(name);
                else
                    TypeLink(writer, type, name);
            }
        }

        void WriteTypeDeclaration(HtmlWriter writer, Type type)
        {
            {
                writer.Write(type.GetAccessModifier().Pretty());
                writer.Write(" ");
                if (type.IsEnum)
                {
                    writer.Write("enum ");
                    TypeLink(writer, type, type.Name);
                    if (type.UnderlyingSystemType != type)
                    {
                        writer.Write(" : ");
                        WriteTypeName(writer, type);
                    }

                    return;
                }
                else if (type.IsInterface)
                {
                    writer.Write("interface ");
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
                else if (type.IsValueType)
                {
                    writer.Write("struct ");
                }
                else
                {
                    writer.Write("<non declared type> " + type.PrettyName(TypeExt.NameMode.Full));
                    return;
                }

                TypeLink(writer, type, type.Name);

                if (type.IsGenericTypeDefinition || type.IsGenericType)
                {
                    WriteGenericArguments(writer, type.GetGenericArguments(), TypeExt.NameMode.WithNamespace);
                }

                bool hasBaseType = false;
                if (type.BaseType != null && type.BaseType != typeof(object))
                {
                    hasBaseType = true;
                    writer.Write(" : ");
                    WriteTypeName(writer, type.BaseType);
                }

                var interfaces = type.GetInterfaces();
                if (interfaces.Length > 0 && !hasBaseType)
                {
                    writer.Write(" : ");
                }

                for (int i = 0; i < interfaces.Length; i++)
                {
                    if (hasBaseType || i > 0)
                        writer.Write(", ");
                    WriteTypeName(writer, interfaces[i]);
                }

                if (type.IsGenericTypeDefinition)
                {
                    var args = type.GetGenericArguments();
                    foreach (var arg in args)
                        WriteGenericConstraints(writer, arg);
                }
            }
        }

        private static IEnumerable<T> ArrayView<T>(T[] array, int start)
        {
            for (int i = start; i < array.Length; i++)
                yield return array[i];
        }

        private static IEnumerable<T> ArrayView<T>(T[] array, int start, int length)
        {
            int end = start + length;
            for (int i = start; i < end; i++)
                yield return array[i];
        }
    }
}
