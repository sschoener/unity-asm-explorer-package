using System;
using System.Collections.Generic;
using System.Linq;

namespace AsmExplorer
{
    public class Namespace {
        private Type[] _types;
        public IEnumerable<Type> Types {
            get { return _types; }
        }

        private List<Namespace> _namespaces;
        public IEnumerable<Namespace> Namespaces {
            get { return _namespaces; }
        }
        public string RelativeName { get; private set; }
        public string FullName { get; private set; }
        public string Name { get; private set; }

        public Assembly Assembly { get; internal set; }

        internal Namespace(string fullName, string relativeName, IEnumerable<Type> types) {
            _types = types.ToArray();
            _namespaces = new List<Namespace>();
            FullName = fullName;
            RelativeName = relativeName;
            int firstDot = fullName.IndexOf('.');
            if (firstDot == -1) {
                Name = fullName;
            } else {
                Name = fullName.Substring(0, firstDot);
            }
        }

        public void AddNamespace(Namespace space) {
            _namespaces.Add(space);
        }

        public Namespace FindNamespace(string name) {
            for (int i = 0; i < _namespaces.Count; i++) {
                if (_namespaces[i].RelativeName == name)
                    return _namespaces[i];
            }
            return null;
        }
    }
}