using System;
using System.Collections.Generic;
using System.Text;

namespace AsmExplorer {
    public static class TypeExt {
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

        public enum NameMode {
            Short,
            WithNamespace,
            Full
        }

        public static string PrettyName(this Type type, NameMode mode=NameMode.Short) {
            string name;
            if (_specialNames.TryGetValue(type, out name))
                return name;
            if (mode == NameMode.Full)
                return type.FullName;
            else if (mode == NameMode.WithNamespace) {
                if (string.IsNullOrEmpty(type.Namespace))
                    return type.Name;
                return type.Namespace + "." + type.Name;
            }
            return type.Name;
        }
    }
}