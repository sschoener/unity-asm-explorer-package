
using System;
using System.Reflection;

namespace AsmExplorer {
    public enum AccessModifier {
        Private,
        PrivateProtected,
        Protected,
        ProtectedInternal,
        Internal,
        Public,
        None, // methods declared on interfaces
    }

    public static class AccessModifierExt {
        public static int CompareTo(this AccessModifier self, AccessModifier other) {
            return ((int)self).CompareTo((int)other);
        }

        public static string Pretty(this AccessModifier am) {
            switch (am) {
                case AccessModifier.Private:
                    return "private";
                case AccessModifier.PrivateProtected:
                    return "private protected";
                case AccessModifier.Protected:
                    return "protected";
                case AccessModifier.ProtectedInternal:
                    return "protected internal";
                case AccessModifier.Internal:
                    return "internal";
                case AccessModifier.Public:
                    return "public";
                case AccessModifier.None:
                    return "";
                default:
                    return "<unknown access modifier>";
            }
        }

        public static AccessModifier GetAccessModifier(this Type type) {
            if (type.IsNestedPrivate)
                return AccessModifier.Private;
            if (type.IsNestedPublic || type.IsPublic)
                return AccessModifier.Public;
            if (type.IsNestedFamANDAssem)
                return AccessModifier.PrivateProtected;
            if (type.IsNestedFamORAssem)
                return AccessModifier.ProtectedInternal;
            if (type.IsNestedFamily)
                return AccessModifier.Protected;
            if (type.IsNestedAssembly || type.IsNotPublic)
                return AccessModifier.Internal;
            return AccessModifier.None;
        }

        public static AccessModifier GetAccessModifier(this FieldInfo field) {
            if (field.IsPrivate)
                return AccessModifier.Private;
            if (field.IsPublic)
                return AccessModifier.Public;
            if (field.IsFamilyAndAssembly)
                return AccessModifier.PrivateProtected;
            if (field.IsFamilyOrAssembly)
                return AccessModifier.ProtectedInternal;
            if (field.IsFamily)
                return AccessModifier.Protected;
            if (field.IsAssembly)
                return AccessModifier.Internal;
            return AccessModifier.None;
        }

        public static AccessModifier GetAccessModifier(this MethodBase method) {
            if (method.IsPrivate)
                return AccessModifier.Private;
            if (method.IsPublic)
                return AccessModifier.Public;
            if (method.IsFamilyAndAssembly)
                return AccessModifier.PrivateProtected;
            if (method.IsFamilyOrAssembly)
                return AccessModifier.ProtectedInternal;
            if (method.IsFamily)
                return AccessModifier.Protected;
            if (method.IsAssembly)
                return AccessModifier.Internal;
            return AccessModifier.None;
        }

        public static AccessModifier GetAccessModifier(this ConstructorInfo method) {
            if (method.IsPrivate)
                return AccessModifier.Private;
            if (method.IsPublic)
                return AccessModifier.Public;
            if (method.IsFamilyAndAssembly)
                return AccessModifier.PrivateProtected;
            if (method.IsFamilyOrAssembly)
                return AccessModifier.ProtectedInternal;
            if (method.IsFamily)
                return AccessModifier.Protected;
            if (method.IsAssembly)
                return AccessModifier.Internal;
            return AccessModifier.None;
        }
    }
}