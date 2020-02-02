using System;
using System.Collections.Generic;
using System.Linq;

namespace AsmExplorer
{
    public class Assembly {
        public readonly string Name;
        public readonly string FullName;
        private Namespace[] _namespaces;
        public IEnumerable<Namespace> Namespaces {
            get { return _namespaces; }
        }

        private Dictionary<string, Namespace> _allNamespaces;
        public IEnumerable<Namespace> AllNamespaces {
            get { return _allNamespaces.Values; }
        }

        private System.Reflection.Assembly _assembly;

        internal Assembly(System.Reflection.Assembly asm, IEnumerable<Namespace> rootNamespaces, Dictionary<string, Namespace> allNamespaces) {
            _assembly = asm;
            Name = asm.GetName().Name;
            FullName = asm.FullName;
            _namespaces = rootNamespaces.ToArray();
            _allNamespaces = allNamespaces;
        }

        public Namespace FindNamespace(string name) {
            Namespace ns;
            if (name == null)
                name = string.Empty;
            _allNamespaces.TryGetValue(name, out ns);
            return ns;
        }

        public Type FindType(string name) {
            return _assembly.GetType(name);
        }
    }
}