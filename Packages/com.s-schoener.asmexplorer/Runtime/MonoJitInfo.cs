using System;
using System.Reflection;

namespace AsmExplorer
{
    public class MonoJitInfo {
        public readonly IntPtr CodeStart;
        public readonly int CodeSize;
        public readonly MethodBase Method;
        public readonly MonoMethod MonoMethod;
        public readonly string Name;

        public MonoJitInfo(MonoMethod monoMethod, MethodBase method, IntPtr codePtr, int codeSize, string name) {
            MonoMethod = monoMethod;
            Method = method;
            CodeStart = codePtr;
            CodeSize = codeSize;
            Name = name;
        }
    }

    public struct MonoIlOffset {
        public uint NativeOffset;
        public uint IlOffset;
    }
}