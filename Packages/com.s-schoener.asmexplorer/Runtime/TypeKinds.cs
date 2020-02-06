using System;

namespace AsmExplorer
{
    enum TypeKind {
        Class,
        Struct,
        Interface,
        Enum,
        StaticClass,        
        Other
    }

    static class TypeKinds {
        public static TypeKind Classify(Type type) {
            if (type.IsValueType)
                return TypeKind.Struct;
            if (type.IsEnum)
                return TypeKind.Enum;
            if (type.IsClass) {
                if (type.IsAbstract && type.IsSealed)
                    return TypeKind.StaticClass;
                return TypeKind.Class;
            }
            if (type.IsInterface)
                return TypeKind.Interface;
            return TypeKind.Other;
        }

        public static string KindName(this TypeKind kind)
        {
            switch (kind)
            {
                case TypeKind.Class:
                    return "Classes";
                case TypeKind.Struct:
                    return "Structs";
                case TypeKind.Interface:
                    return "Interfaces";
                case TypeKind.Enum:
                    return "Enums";
                case TypeKind.StaticClass:
                    return "Static classes";
                case TypeKind.Other:
                    return "Others";
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }
        }
    }
}