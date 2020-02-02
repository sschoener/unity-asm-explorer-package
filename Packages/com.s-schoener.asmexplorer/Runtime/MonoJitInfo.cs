using System;
using System.Reflection;

namespace AsmExplorer
{
    public class MonoJitInfo {
        public readonly IntPtr CodeStart;
        public readonly int CodeSize;
        public readonly MethodBase Method;

        public MonoJitInfo(MethodBase method, IntPtr codePtr, int codeSize) {
            Method = method;
            CodeStart = codePtr;
            CodeSize = codeSize;
        }
    }
}