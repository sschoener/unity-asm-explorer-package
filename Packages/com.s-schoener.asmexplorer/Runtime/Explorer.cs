using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace AsmExplorer
{
    public class Explorer {
        private Dictionary<string, Assembly> _assemblies;
        public IEnumerable<Assembly> Assemblies {
            get { return _assemblies.Values; }
        }

        public Explorer() {
            InitAssemblies();
        }

        private void InitAssemblies() {
            _assemblies = new Dictionary<string, Assembly>();
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++) {
                RegisterAssembly(assemblies[i]);
            }
            AppDomain.CurrentDomain.AssemblyLoad += (o, args) => RegisterAssembly(args.LoadedAssembly);
        }

        private void RegisterAssembly(System.Reflection.Assembly asm) {
            var types = asm.GetTypes();
            var typesByNamespace = new Dictionary<string, List<Type>>();
            for (int i = 0; i < types.Length; i++) {
                List<Type> list;
                string n = types[i].Namespace ?? "";
                if (!typesByNamespace.TryGetValue(n, out list)) {
                    list = new List<Type>();
                    typesByNamespace[n] = list;
                }
                list.Add(types[i]);
            }

            // sort namespaces by nesting and reconstruct their tree-structure
            var namespaces = new Dictionary<string, Namespace>();
            var roots = new Dictionary<string, Namespace>();
            string[] keys = typesByNamespace.Keys.ToArray();
            Array.Sort(keys, (lhs, rhs) => lhs.Count(c => c == '.').CompareTo(rhs.Count(c => c == '.')));
            for (int i = 0; i < keys.Length; i++) {
                var path = keys[i];
                int lastIdx = path.LastIndexOf('.');
                while (lastIdx != -1) {
                    string prefix = path.Substring(0, lastIdx);
                    Namespace parent;
                    if (namespaces.TryGetValue(prefix, out parent)) {
                        string suffix = path.Substring(lastIdx + 1);
                        var ns = new Namespace(path, suffix, typesByNamespace[path]);
                        parent.AddNamespace(ns);
                        namespaces.Add(path, ns);
                        break;
                    }
                    lastIdx = prefix.LastIndexOf('.');
                }
                if (lastIdx == -1) {
                    var ns = new Namespace(path, path, typesByNamespace[path]);
                    namespaces.Add(path, ns);
                    roots.Add(path, ns);
                }
            }
            var assembly = new Assembly(asm, roots.Values, namespaces);
            foreach (var ns in namespaces.Values) {
                ns.Assembly = assembly;
            }
            _assemblies.Add(assembly.FullName, assembly);
        }

        public Assembly FindAssembly(string name) {
            Assembly result;
            if (!_assemblies.TryGetValue(name, out result))
                return null;
            return result;
        }
    }
}