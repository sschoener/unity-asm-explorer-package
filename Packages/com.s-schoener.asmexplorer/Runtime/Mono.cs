using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Diagnostics.Tracing.Parsers.FrameworkEventSource;

namespace AsmExplorer
{
    public static unsafe class Mono
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        // used to be mono.dll for .NET 3.5 and earlier
        public const string MonoDllName = "mono-2.0-bdwgc.dll";
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        public const string MonoDllName = "libmonobdwgc-2.0.dylib";
#endif

        [DllImport(MonoDllName, CallingConvention = CallingConvention.FastCall, EntryPoint = "mono_domain_get", CharSet = CharSet.Ansi)]
        static extern IntPtr GetDomain();

        [DllImport(MonoDllName, EntryPoint = "mono_jit_info_table_find", CharSet = CharSet.Ansi)]
        static extern IntPtr FindJitInfo(IntPtr domain, IntPtr instructionPointer);

        [DllImport(MonoDllName, EntryPoint = "mono_jit_info_get_code_start", CharSet = CharSet.Ansi)]
        static extern IntPtr GetCodeStart(IntPtr jitInfo);

        [DllImport(MonoDllName, EntryPoint = "mono_jit_info_get_code_size", CharSet = CharSet.Ansi)]
        static extern int GetCodeSize(IntPtr jitInfo);

        [DllImport(MonoDllName, EntryPoint = "mono_jit_info_get_method", CharSet = CharSet.Ansi)]
        static extern IntPtr GetMethod(IntPtr jitInfo);

        [DllImport(MonoDllName, EntryPoint = "mono_pmip", CharSet = CharSet.Ansi)]
        static extern sbyte* PMip_Internal(IntPtr instructionPointer);

        [DllImport(MonoDllName, EntryPoint = "mono_method_get_name", CharSet = CharSet.Ansi)]
        static extern IntPtr GetMethodName(IntPtr monoMethod);

        [DllImport(MonoDllName, EntryPoint = "mono_method_get_header", CharSet = CharSet.Ansi)]
        static extern IntPtr GetMethodHeader(IntPtr monoMethod);

        [DllImport(MonoDllName, EntryPoint = "mono_metadata_free_mh", CharSet = CharSet.Ansi)]
        static extern void FreeMethodHeader(IntPtr methodHeader);

        [DllImport(MonoDllName, EntryPoint = "mono_method_header_get_code", CharSet = CharSet.Ansi)]
        static extern byte* GetIlCode(IntPtr methodHeader, uint* codeSize, uint* maxStack); // returns const unsigned char*

        [DllImport(MonoDllName, EntryPoint = "mono_opcode_value", CharSet = CharSet.Ansi)]
        static extern int GetIlOpcodeValue(byte** ip, byte* end); // return MonoOpcodeEnum

        [DllImport(MonoDllName, EntryPoint = "mono_domain_foreach", CharSet = CharSet.Ansi)]
        static extern void MonoDomainForeach(IntPtr func, void* userData);
        [DllImport(MonoDllName, EntryPoint = "mono_domain_get_id", CharSet = CharSet.Ansi)]
        static extern int MonoDomainGetId(IntPtr domain);
        [DllImport(MonoDllName, EntryPoint = "mono_domain_get_by_id", CharSet = CharSet.Ansi)]
        static extern IntPtr MonoDomainGetById(int id);
        [DllImport(MonoDllName, EntryPoint = "mono_domain_get_friendly_name", CharSet = CharSet.Ansi)]
        static extern byte* MonoDomainGetFriendlyName(IntPtr domain);

        static void FindJitInfo_Iterator(IntPtr domain, void* userData)
        {
            var jitLookup = (MonoJitLookup*)userData;
            if (jitLookup->JitInfo != IntPtr.Zero)
                return;
            var jitInfo = FindJitInfo(domain, jitLookup->InstructionPointer);
            if (jitInfo != IntPtr.Zero)
            {
                jitLookup->JitInfo = jitInfo;
                jitLookup->DomainId = MonoDomainGetId(domain);
            }
        }

        struct MonoJitLookup
        {
            public IntPtr InstructionPointer;
            public IntPtr JitInfo;
            public int DomainId;
        }

        public static void ForceCompilation(MethodInfo method)
        {
            // this enforces compilation
            method.MethodHandle.GetFunctionPointer();
        }

        internal static IntPtr Domain => GetDomain();

        static ConstructorInfo _methodHandleCtor;
        static object[] _parameterTmp;

        static RuntimeMethodHandle MakeMethodHandle(IntPtr monoMethod)
        {
            if (_methodHandleCtor == null)
            {
                _methodHandleCtor = typeof(RuntimeMethodHandle).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null, new Type[] { typeof(IntPtr) }, null);
                if (_parameterTmp == null)
                    _parameterTmp = new object[1];
            }
            _parameterTmp[0] = monoMethod;
            return (RuntimeMethodHandle)_methodHandleCtor.Invoke(_parameterTmp);
        }

        static ConstructorInfo _methodInfoCtor;

        static MethodInfo MakeMethodInfo(RuntimeMethodHandle handle)
        {
            if (_methodInfoCtor == null)
            {
                var type = Type.GetType("System.Reflection.MonoMethod");
                if (type == null) // this is necessary for newer versions of Unity
                    type = Type.GetType("System.Reflection.RuntimeMethodInfo");
                _methodInfoCtor = type.GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null, new Type[] { typeof(RuntimeMethodHandle) }, null);
                if (_parameterTmp == null)
                    _parameterTmp = new object[1];
            }
            _parameterTmp[0] = handle;
            return (MethodInfo)_methodInfoCtor.Invoke(_parameterTmp);
        }

        static ConstructorInfo s_CtorInfoCtor;
        static Type s_MonoCtorType;
        static FieldInfo s_CtorHandleField;

        static ConstructorInfo MakeCtorInfo(IntPtr handle)
        {
            if (s_MonoCtorType == null)
            {
                s_MonoCtorType = Type.GetType("System.Reflection.MonoCMethod");
                if (s_MonoCtorType == null) // this is necessary for newer versions of Unity
                    s_MonoCtorType = Type.GetType("System.Reflection.RuntimeConstructorInfo");
                s_CtorHandleField = s_MonoCtorType.GetField("mhandle", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            }
            object ctor = Activator.CreateInstance(s_MonoCtorType);
            s_CtorHandleField.SetValue(ctor, handle);
            return (ConstructorInfo)ctor;
        }

        static IntPtr s_FindJitInfoFunctionPtr;
        public static MonoJitInfo GetJitInfoAnyDomain(IntPtr instructionPointer, out int domainId)
        {
            var lookup = new MonoJitLookup
            {
                InstructionPointer = instructionPointer
            };
            domainId = 0;
            if (s_FindJitInfoFunctionPtr == IntPtr.Zero)
            {
                var method = typeof(Mono).GetMethod(nameof(FindJitInfo_Iterator), BindingFlags.Static | BindingFlags.NonPublic);
                s_FindJitInfoFunctionPtr = method.MethodHandle.GetFunctionPointer();
            }
            MonoDomainForeach(s_FindJitInfoFunctionPtr, &lookup);
            return ResolveJitInfo(lookup.JitInfo);
        }

        static MonoJitInfo ResolveJitInfo(IntPtr jitInfo)
        {
            if (jitInfo.ToInt64() == 0)
                return new MonoJitInfo(new MonoMethod(), null, (IntPtr)null, 0, null);
            IntPtr codeStart = GetCodeStart(jitInfo);
            int codeSize = GetCodeSize(jitInfo);
            IntPtr monoMethod = GetMethod(jitInfo);
            MethodBase method = null;
            string name = null;
            if (monoMethod != (IntPtr)null)
            {
                // For some reason, adding marshalling annotations to GetMethodName
                // causes crashes. This is the workaround.
                var namePtr = GetMethodName(monoMethod);
                name = Marshal.PtrToStringAnsi(namePtr);
                if (name == ".ctor" || name == ".cctor")
                    method = MakeCtorInfo(monoMethod);
                else
                    method = MakeMethodInfo(MakeMethodHandle(monoMethod));
            }
            return new MonoJitInfo(new MonoMethod(monoMethod), method, codeStart, codeSize, name);
        }

        public static MonoJitInfo GetJitInfo(IntPtr instructionPointer)
        {
            var jitInfo = FindJitInfo(GetDomain(), instructionPointer);
            return ResolveJitInfo(jitInfo);
        }

        public static MonoJitInfo GetJitInfo(MethodBase method)
        {
            // this forces compilation
            IntPtr functionPtr = method.MethodHandle.GetFunctionPointer();
            return GetJitInfo(functionPtr);
        }
    }
}