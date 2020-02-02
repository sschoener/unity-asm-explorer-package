using System;
using System.Reflection;
using System.Runtime.InteropServices;

namespace AsmExplorer
{
    public static class Mono
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        // used to be mono.dll for .NET 3.5 and earlier
        const string MonoDllName = "mono-2.0-bdwgc.dll";
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        const string MonoDllName = "libmonobdwgc-2.0.dylib";
#endif

        [DllImport(MonoDllName, CallingConvention = CallingConvention.FastCall, EntryPoint = "mono_domain_get", CharSet = CharSet.Ansi)]
        private static extern IntPtr GetDomain();

        [DllImport(MonoDllName, EntryPoint="mono_jit_info_table_find", CharSet = CharSet.Ansi)]
        private static extern IntPtr FindJitInfo(IntPtr domain, IntPtr instructionPointer);
        
        [DllImport(MonoDllName, EntryPoint="mono_jit_info_get_code_start", CharSet = CharSet.Ansi)]
        private static extern IntPtr GetCodeStart(IntPtr jitInfo);

        [DllImport(MonoDllName, EntryPoint="mono_jit_info_get_code_size", CharSet = CharSet.Ansi)]
        private static extern int GetCodeSize(IntPtr jitInfo);

        [DllImport(MonoDllName, EntryPoint="mono_jit_info_get_method", CharSet = CharSet.Ansi)]
        private static extern IntPtr GetMethod(IntPtr jitInfo);

        [DllImport(MonoDllName, EntryPoint="mono_method_get_name", CharSet = CharSet.Ansi)]
        private static extern IntPtr GetMethodName(IntPtr monoMethod);

        public static void ForceCompilation(MethodInfo method) {
            // this enforces compilation
            method.MethodHandle.GetFunctionPointer();
        }

        private static ConstructorInfo _methodHandleCtor;
        private static object[] _parameterTmp;
        private static RuntimeMethodHandle MakeMethodHandle(IntPtr monoMethod) {
            if (_methodHandleCtor == null) {
                _methodHandleCtor = typeof(RuntimeMethodHandle).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null, new Type[] { typeof(IntPtr) }, null);
                if (_parameterTmp == null)
                    _parameterTmp = new object[1];
            }
            _parameterTmp[0] = monoMethod;
            return (RuntimeMethodHandle)_methodHandleCtor.Invoke(_parameterTmp);
        }

        private static ConstructorInfo _methodInfoCtor;
        private static MethodInfo MakeMethodInfo(RuntimeMethodHandle handle) {
            if (_methodInfoCtor == null) {
                _methodInfoCtor = Type.GetType("System.Reflection.MonoMethod").GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null, new Type[] { typeof(RuntimeMethodHandle) }, null);
                if (_parameterTmp == null)
                    _parameterTmp = new object[1];
            }
            _parameterTmp[0] = handle;
            return (MethodInfo) _methodInfoCtor.Invoke(_parameterTmp);
        }

        private static ConstructorInfo _ctorInfoCtor;
        private static Type _monoCtorType;
        private static FieldInfo _ctorHandleField;
        private static ConstructorInfo MakeCtorInfo(IntPtr handle) {
            if (_monoCtorType == null) {
                _monoCtorType = Type.GetType("System.Reflection.MonoCMethod");
                _ctorHandleField = _monoCtorType.GetField("mhandle", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            }
            object ctor = Activator.CreateInstance(_monoCtorType);
            _ctorHandleField.SetValue(ctor, handle);
            return (ConstructorInfo)ctor;
        }

        public static MonoJitInfo GetJitInfo(IntPtr instructionPointer) {
            var jitInfo = FindJitInfo(GetDomain(), instructionPointer);
            if (jitInfo.ToInt64() == 0) {
                return new MonoJitInfo(null, (IntPtr) null, 0);
            }
            IntPtr codeStart = GetCodeStart(jitInfo);
            int codeSize = GetCodeSize(jitInfo);
            IntPtr monoMethod = GetMethod(jitInfo);
            MethodBase method = null;
            if (monoMethod != (IntPtr) null) {
                // For some reason, adding marshalling annotations to GetMethodName
                // causes crashes. This is the workaround.
                var namePtr = GetMethodName(monoMethod);
                string name = Marshal.PtrToStringAnsi(namePtr);
                if (name == ".ctor" || name == ".cctor")
                    method = MakeCtorInfo(monoMethod);
                else
                    method = MakeMethodInfo(MakeMethodHandle(monoMethod));
            }                
            return new MonoJitInfo(method, codeStart, codeSize);
        }

        public static MonoJitInfo GetJitInfo(MethodBase method) {
            // this forces compilation
            IntPtr functionPtr = method.MethodHandle.GetFunctionPointer();
            return GetJitInfo(functionPtr);
		}
    }
}