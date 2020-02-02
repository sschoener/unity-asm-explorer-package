using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace AsmExplorer
{
    public partial class WebService {
        private void InspectType(HtmlWriter writer, string assemblyName, string typeName) {
            var asm = _explorer.FindAssembly(assemblyName);
            if (asm == null) {
                writer.Write("Unknown assembly name " + assemblyName);
                return;
            }
            var type = asm.FindType(typeName);
            if (type == null) {
                writer.Write("Unknown type name " + typeName + " in " + asm.FullName);
                return;
            }
            InspectType(writer, asm, type);
        }

        private void InspectType(HtmlWriter writer, Assembly assembly, Type type) {
            var asm = assembly;
            using(writer.Tag("small")) {
                AssemblyLink(writer, asm);
                writer.Write(" | ");
                NamespaceLink(writer, asm.FindNamespace(type.Namespace), type.Namespace ?? "<root>");
                // see whether this is a nested type
                if (type.DeclaringType != null) {
                    writer.Write(" | ");
                    TypeLink(writer, type.DeclaringType);
                }
            }
            var attr = type.GetCustomAttributes(true);
            if (attr.Length > 0) {
                writer.Break();
                writer.Break();
                WriteAttributes(writer, attr);
            }
            
            using (writer.Tag("h2")) {
                writer.Write(type.PrettyName(TypeExt.NameMode.WithNamespace));
                if (type.IsGenericType)
                    WriteGenericArguments(writer, type.GetGenericArguments(), TypeExt.NameMode.WithNamespace);
            }

            if (type.IsGenericType) {
                TypeLink(writer, type.GetGenericTypeDefinition(), "go to generic definition " + type.Name);
                writer.Break();
            }
            WriteTypeDeclaration(writer, type);
            if (type.IsClass || type.IsValueType) {
                InspectClass(writer, type);
            } else if (type.IsInterface) {
                InspectInterface(writer, type);
            } else if (type.IsEnum) {
                InspectEnum(writer, type);
            }
        }

        private void InspectEnum(HtmlWriter writer, Type type)
        {
            var fields = type.GetFields();
            MakeTable(
                writer,
                fields.Where(f => f.IsLiteral),
                f => writer.Write(f.Name),
                f => {
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

        private void InspectClass(HtmlWriter writer, Type type) {
            var instanceCtors = type.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Array.Sort(instanceCtors, (lhs, rhs) => ReflectionHelper.CompareConstructors(type, lhs, rhs));
            writer.Inline("h4", "Constructors");
            LayoutCtors(writer, instanceCtors);

            LayoutInstanceFields(writer, type);
            LayoutInstanceProperties(writer, type);
            LayoutInstanceMethods(writer, type);

            var staticCtor = type.GetConstructors(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            Array.Sort(instanceCtors, (lhs, rhs) => ReflectionHelper.CompareConstructors(type, lhs, rhs));
            writer.Inline("h4", "Static Constructor");
            LayoutCtors(writer, staticCtor);

            writer.Inline("h4", "Static Fields");
            var staticFields = type.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            Array.Sort(staticFields, (lhs, rhs) => ReflectionHelper.CompareFields(type, lhs, rhs));
            LayoutStaticFields(writer, staticFields);

            writer.Inline("h4", "Static Properties");
            var staticProperties = type.GetProperties(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            Array.Sort(staticProperties, (lhs, rhs) => ReflectionHelper.CompareProperties(type, lhs, rhs));
            LayoutProperties(writer, staticProperties);

            writer.Inline("h4", "Static Functions");
            var staticMethods = type.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            Array.Sort(staticMethods, (lhs, rhs) => ReflectionHelper.CompareMethods(type, lhs, rhs));
            LayoutMethods(writer, staticMethods.Where(m => !m.IsSpecialName));

            LayoutNestedTypes(writer, type);
        }

        private void LayoutFields(HtmlWriter writer, IEnumerable<FieldInfo> fields) {
            MakeTable(
                writer,
                fields,
                f => {
                    writer.Write(f.GetAccessModifier().Pretty());
                    if (f.IsLiteral)
                        writer.Write(" const");
                    else if (f.IsStatic)
                        writer.Write(" static");
                    else if (f.IsInitOnly)
                        writer.Write(" readonly");
                },
                f => {
                    if (f.FieldType.IsByRef) {
                        writer.Write("ref ");
                        WriteTypeName(writer, f.FieldType.GetElementType());
                    } else {
                        WriteTypeName(writer, f.FieldType);
                    }
                },
                f => writer.Write(f.Name),
                f => WriteAttributes(writer, f.GetCustomAttributes(true), false)
            );
        }

        private void LayoutStaticFields(HtmlWriter writer, IEnumerable<FieldInfo> fields) {
            MakeTable(
                writer,
                fields,
                f => {
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
                f => writer.Write(f.GetValue(null).ToString()),
                f => WriteAttributes(writer, f.GetCustomAttributes(true), false)
            );
        }

        private void LayoutNestedTypes(HtmlWriter writer, Type type) {
            writer.Inline("h4", "Nested Types");
            var nested = type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic);
            MakeTable(
                writer,
                nested,
                t => WriteTypeDeclaration(writer, t)
            );
        }

        private void LayoutInstanceMethods(HtmlWriter writer, Type type) {
            var instanceMethods = type.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Array.Sort(instanceMethods, (lhs, rhs) => ReflectionHelper.CompareMethods(type, lhs, rhs));
            int split = instanceMethods.Length;
            for (int i = 0; i < instanceMethods.Length; i++) {
                if (instanceMethods[i].DeclaringType != type) {
                    split = i;
                    break;
                }
            }
            writer.Inline("h4", "Instance Methods");
            if (split > 0) {
                LayoutMethods(writer, ArrayView(instanceMethods, 0, split).Where(m => !m.IsSpecialName));
            }
            writer.Inline("h4", "Inherited Methods");
            if (split < instanceMethods.Length) {
                LayoutMethods(writer, ArrayView(instanceMethods, split).Where(m => !m.IsSpecialName));
            }
        }

        private void LayoutInstanceProperties(HtmlWriter writer, Type type){
            var instanceProperties = type.GetProperties(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Array.Sort(instanceProperties, (lhs, rhs) => ReflectionHelper.CompareProperties(type, lhs, rhs));
            int split = instanceProperties.Length;
            for (int i = 0; i < instanceProperties.Length; i++) {
                if (instanceProperties[i].DeclaringType != type) {
                    split = i;
                    break;
                }
            }
            writer.Inline("h4", "Instance Properties");
            if (split > 0) {
                LayoutProperties(writer, ArrayView(instanceProperties, 0, split));
            }
            writer.Inline("h4", "Inherited Properties");
            if (split < instanceProperties.Length) {
                LayoutProperties(writer, ArrayView(instanceProperties, split));
            }
        }

        private void LayoutInstanceFields(HtmlWriter writer, Type type) {
            var instanceFields = type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Array.Sort(instanceFields, (lhs, rhs) => ReflectionHelper.CompareFields(type, lhs, rhs));
            int split = instanceFields.Length;
            for (int i = 0; i < instanceFields.Length; i++) {
                if (instanceFields[i].DeclaringType != type) {
                    split = i;
                    break;
                }
            }
            writer.Inline("h4", "Instance Fields");
            if (split > 0) {
                LayoutFields(writer, ArrayView(instanceFields, 0, split));
            }
            writer.Inline("h4", "Inherited Fields");
            if (split < instanceFields.Length) {
                LayoutFields(writer, ArrayView(instanceFields, split));
            }
        }

        private void LayoutProperties(HtmlWriter writer, IEnumerable<PropertyInfo> properties) {
            MakeTable(
                writer,
                properties,
                p => WriteTypeName(writer, p.PropertyType),
                p => {
                    writer.Write(p.Name);
                    writer.Write(" { ");
                    if (p.CanRead) {
                        var getter = p.GetGetMethod(nonPublic: true);
                        WriteMethodPrefix(writer, getter);
                        writer.Write(" ");
                        FunctionLink(writer, getter, "get");
                        writer.Write("; ");
                    }
                    if (p.CanWrite) {
                        var setter = p.GetSetMethod(nonPublic: true);
                        WriteMethodPrefix(writer, setter);
                        writer.Write(" ");
                        FunctionLink(writer, setter, "set");
                        writer.Write(";");
                    }
                    writer.Write(" } ");
                },
                p => WriteAttributes(writer, p.GetCustomAttributes(true), false)
            );
        }

        private void WriteGenericArguments(HtmlWriter writer, Type[] types, TypeExt.NameMode mode=TypeExt.NameMode.Short) {
            writer.Write("< ");
            for (int i = 0; i < types.Length; i++) {
                if (i > 0)
                    writer.Write(", ");
                if (types[i].IsGenericParameter){
                    var gpa = types[i].GenericParameterAttributes;
                    if ((gpa & GenericParameterAttributes.Covariant) != 0)
                        writer.Write("out ");
                    else if ((gpa & GenericParameterAttributes.Contravariant) != 0)
                        writer.Write("in ");
                    writer.Write(types[i].Name);
                } else {
                    TypeLink(writer, types[i], types[i].PrettyName(mode));
                }
            }
            writer.Write(" >");
        }

        private void WriteGenericConstraints(HtmlWriter writer, Type generic) {
            var constraints = generic.GetGenericParameterConstraints();
            if (constraints.Length == 0)
                return;
            writer.Write(" where ");
            writer.Write(generic.Name);
            writer.Write(" : ");
            var gpa = generic.GenericParameterAttributes;
            for (int i = 0; i < constraints.Length; i++) {
                if (i > 0) writer.Write(", ");
                WriteTypeName(writer, constraints[i]);
            }
            bool hasPrevious = constraints.Length > 0;
            if ((gpa & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0) {
                if (hasPrevious) writer.Write(", ");
                writer.Write("struct");
                hasPrevious = true;
            }
            if ((gpa & GenericParameterAttributes.ReferenceTypeConstraint) != 0) {
                if (hasPrevious) writer.Write(", ");
                writer.Write("class");
                hasPrevious = true;
            }
            if ((gpa & GenericParameterAttributes.DefaultConstructorConstraint) != 0) {
                if (hasPrevious) writer.Write(", ");
                writer.Write("new()");
                hasPrevious = true;
            }
        }

        private void LayoutMethods(HtmlWriter writer, IEnumerable<MethodInfo> methods) {
            MakeTable(
                writer,
                methods,
                m => WriteMethodPrefix(writer, m),
                m => WriteMethodReturnType(writer, m),
                m => WriteMethodDeclaration(writer, m)
            );
        }

        private void WriteParameter(HtmlWriter writer, ParameterInfo p) {
            var pt = p.ParameterType;
            if (p.IsIn) {
                writer.Write("in ");
            }
            if (p.IsOut) {
                writer.Write("out ");
            } else if (p.ParameterType.IsByRef) {
                if (!p.IsOut && !p.IsIn)
                    writer.Write("ref ");
                pt = p.ParameterType.GetElementType();
            }
            WriteTypeName(writer, pt);
            writer.Write(" ");
            writer.Write(p.Name);
        }

        private void LayoutCtors(HtmlWriter writer, IEnumerable<ConstructorInfo> methods) {
            MakeTable(
                writer,
                methods,
                c => WriteCtorPrefix(writer, c),
                c => WriteCtorDeclaration(writer, c)
            );
        }

        private static Dictionary<Type, string> _specialNames = new Dictionary<Type, string>() {
          {typeof(long), "long"},
          {typeof(int), "int"},
          {typeof(short), "short"},
          {typeof(sbyte), "sbyte"},
          {typeof(ulong), "ulong"},
          {typeof(uint), "uint"},
          {typeof(ushort), "ushort"},
          {typeof(byte), "byte"},
          {typeof(void), "void"},
          {typeof(string), "string"},
          {typeof(bool), "bool"},
          {typeof(object), "object"}
        };
        private void WriteTypeName(HtmlWriter writer, Type type, bool full=true) {
            if(type.IsGenericParameter) {
                writer.Write(type.Name);
                return;
            }
            string name;
            if (_specialNames.TryGetValue(type, out name)) {
                TypeLink(writer, type, name);
                return;
            } else if (type.IsGenericType) {
                TypeLink(writer, type, full ? type.Namespace + "." + type.Name : type.Name);
                writer.Write("< ");
                var arguments = type.GetGenericArguments();
                for (int i = 0; i < arguments.Length; i++) {
                    if (i > 0)
                        writer.Write(", ");
                    WriteTypeName(writer, arguments[i], full);
                }
                writer.Write(" >");
            } else if (type.IsPointer) {
                writer.Write("*");
                WriteTypeName(writer, type.GetElementType(), full);
            } else {
                if (full) {
                    name = type.FullName ?? type.Name;
                } else {
                    name = type.Name ?? type.FullName;
                }
                TypeLink(writer, type, name);
                return;
            }
        }

        public void WriteTypeDeclaration(HtmlWriter writer, Type type) {
            writer.Write(type.GetAccessModifier().Pretty());
            writer.Write(" ");
            if (type.IsEnum) {
                writer.Write("enum ");
                TypeLink(writer, type, type.Name);
                if (type.UnderlyingSystemType != type) {
                    writer.Write(" : ");
                    WriteTypeName(writer, type);
                }
                return;
            } else if (type.IsInterface) {
                writer.Write("interface ");
            } else if (type.IsClass) {
                if (type.IsAbstract) {
                    writer.Write("abstract ");
                }
                if (type.IsSealed) {
                    writer.Write("sealed ");
                }
                writer.Write("class ");
            } else if (type.IsValueType) {
                writer.Write("struct ");
            } else {
                writer.Write("<non declared type> " + type.PrettyName(TypeExt.NameMode.Full));
                return;
            }

            TypeLink(writer, type, type.Name);

            if (type.IsGenericTypeDefinition || type.IsGenericType) {
                WriteGenericArguments(writer, type.GetGenericArguments(), TypeExt.NameMode.WithNamespace);
            }

            bool hasBaseType = false;
            if (type.BaseType != null && type.BaseType != typeof(object)) {
                hasBaseType = true;
                writer.Write(" : ");
                WriteTypeName(writer, type.BaseType);
            }
            var interfaces = type.GetInterfaces();
            if (interfaces.Length > 0 && !hasBaseType) {
                writer.Write(" : ");
            }
            for (int i = 0; i < interfaces.Length; i++) {
                if (hasBaseType || i > 0)
                    writer.Write(", ");
                WriteTypeName(writer, interfaces[i]);
            }

            if (type.IsGenericTypeDefinition) {
                var args = type.GetGenericArguments();
                foreach (var arg in args)
                    WriteGenericConstraints(writer, arg);
            }
        }

        private static IEnumerable<T> ArrayView<T>(T[] array, int start) {
            for (int i = start; i < array.Length; i++)
                yield return array[i];
        }

        private static IEnumerable<T> ArrayView<T>(T[] array, int start, int length) {
            int end = start + length;
            for (int i = start; i < end; i++)
                yield return array[i];
        }
    }
}