using System;
using System.Reflection;

namespace AsmExplorer {
    public static class ReflectionHelper {
        public static int CompareFields(Type type, FieldInfo lhs, FieldInfo rhs) {
            int c = CompareBool(lhs.IsStatic, rhs.IsStatic);
            if (c != 0) return c;
            c = CompareBool(lhs.DeclaringType != type, rhs.DeclaringType != type);
            if (c != 0) return c;
            c = lhs.GetAccessModifier().CompareTo(rhs.GetAccessModifier());
            if (c != 0) return c;
            return lhs.Name.CompareTo(rhs.Name);
        }

        public static int CompareBool(bool lhs, bool rhs) {
            if (lhs && !rhs)
                return 1;
            if (!lhs && rhs)
                return -1;
            return 0;
        }

        public static int CompareConstructors(Type type, ConstructorInfo lhs, ConstructorInfo rhs) {
            int c = CompareBool(lhs.IsStatic, rhs.IsStatic);
            if (c != 0) return c;
            c = CompareBool(lhs.DeclaringType != type, rhs.DeclaringType != type);
            if (c != 0) return c;
            c = lhs.GetAccessModifier().CompareTo(rhs.GetAccessModifier());
            if (c != 0) return c;
            c = CompareBool(lhs.IsAbstract, rhs.IsAbstract);
            if (c != 0) return c;
            return lhs.Name.CompareTo(rhs.Name);
        }

        public static int CompareMethods(Type type, MethodInfo lhs, MethodInfo rhs) {
            int c = CompareBool(lhs.IsStatic, rhs.IsStatic);
            if (c != 0) return c;
            c = CompareBool(lhs.DeclaringType != type, rhs.DeclaringType != type);
            if (c != 0) return c;
            c = lhs.GetAccessModifier().CompareTo(rhs.GetAccessModifier());
            if (c != 0) return c;
            c = CompareBool(lhs.IsAbstract, rhs.IsAbstract);
            if (c != 0) return c;
            return lhs.Name.CompareTo(rhs.Name);
        }

        public static int CompareProperties(Type type, PropertyInfo lhs, PropertyInfo rhs) {
            int c = CompareBool(IsStatic(lhs), IsStatic(rhs));
            if (c != 0) return c;
            c = CompareBool(lhs.DeclaringType != type, rhs.DeclaringType != type);
            if (c != 0) return c;
            return lhs.Name.CompareTo(rhs.Name);
        }

        public static bool IsStatic(this PropertyInfo info) {
            if (info.CanRead) {
                var getter = info.GetGetMethod();
                if (getter != null)
                    return getter.IsStatic;
            }
            var setter = info.GetSetMethod();
            if (setter != null)
                return setter.IsStatic;
            return false;
        }

        public static bool IsPropertyMethod(this MethodInfo method) {
            return method.IsSpecialName && (method.Name.StartsWith("get_") || method.Name.StartsWith("set_"));
        }
    }
}