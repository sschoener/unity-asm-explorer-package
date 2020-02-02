using System;
using System.Reflection;

namespace AsmExplorer
{
    public class MonoJitInfo {
        public readonly IntPtr CodeStart;
        public readonly int CodeSize;
        public readonly MethodBase Method;
        public readonly MonoMethod MonoMethod;

        public MonoJitInfo(MonoMethod monoMethod, MethodBase method, IntPtr codePtr, int codeSize) {
            MonoMethod = monoMethod;
            Method = method;
            CodeStart = codePtr;
            CodeSize = codeSize;
        }
    }

    public struct MonoIlOffset {
        public uint NativeOffset;
        public uint IlOffset;
    }
}