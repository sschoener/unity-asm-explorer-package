using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;

namespace AsmExplorer
{
    internal static unsafe class MonoOpcodes
    {
        [DllImport("kernel32.dll")]
        private static extern IntPtr LoadLibrary(string filename);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procname);

        [DllImport("libdl.so")]
        private static extern IntPtr dlopen(string filename, int flags);

        [DllImport("libdl.so")]
        private static extern IntPtr dlsym(IntPtr handle, string symbol);

        const string OpcodesSymbol = "mono_opcodes";

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        static IntPtr LoadOpcodes() {
            var handle = LoadLibrary(Mono.MonoDllName);
            if (handle == IntPtr.Zero)
                throw new InvalidOperationException($"Failed to load library {Mono.MonoDllName}");
            var address = GetProcAddress(handle, OpcodesSymbol);
            if (address == IntPtr.Zero)
                throw new InvalidOperationException($"Failed to find symbol {OpcodesSymbol} in library {Mono.MonoDllName}");
            return address;
        }
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        static IntPtr LoadOpcodes() {
            const int RTLD_NOW = 2;
            var handle = dlopen(Mono.MonoDllName, RTLD_NOW);
            if (handle == IntPtr.Zero)
                throw new InvalidOperationException($"Failed to load library {Mono.MonoDllName}");
            var address = dlsym(handle, OpcodesSymbol);
            if (address == IntPtr.Zero)
                throw new InvalidOperationException($"Failed to find symbol {OpcodesSymbol} in library {Mono.MonoDllName}");
            return address;
        }
#endif

        static MonoOpcode[] m_Opcodes;

        [DllImport(Mono.MonoDllName, EntryPoint = "mono_opcode_name", CharSet = CharSet.Ansi)]
        private static extern IntPtr GetIlOpcodeName(int opcode); // returns const char*

#pragma warning disable 649
        struct MonoOpcode_Internal
        {
            public byte Argument;
            public byte FlowType;
            public ushort OpVal;
        }
#pragma warning restore 649

        static unsafe void CopyOpcodes()
        {

            int numOpcodes;
            List<string> opcodeNames = new List<string>();

            {
                ushort c = 0;
                IntPtr opcodeName;
                while ((opcodeName = GetIlOpcodeName(c)) != null)
                {
                    opcodeNames.Add(Marshal.PtrToStringAnsi(opcodeName));
                    c++;
                }
                numOpcodes = c;
            }

            m_Opcodes = new MonoOpcode[numOpcodes];
            var ptr = (MonoOpcode_Internal*)LoadOpcodes();

            for (int op = 0; op < numOpcodes; op++)
            {
                m_Opcodes[op] = new MonoOpcode
                {
                    OpcodeName = opcodeNames[op],
                    Argument = ptr[op].Argument,
                    FlowType = ptr[op].FlowType,
                    OpVal = ptr[op].OpVal
                };
            }
        }

        public static MonoOpcode[] IlOpcodes
        {
            get
            {
                if (m_Opcodes == null)
                    CopyOpcodes();
                return m_Opcodes;
            }
        }

    }
}