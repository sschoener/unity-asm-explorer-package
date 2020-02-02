using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace AsmExplorer
{
    public static class MonoDebug {

        [DllImport(Mono.MonoDllName, EntryPoint="mono_debug_enabled", CharSet = CharSet.Ansi)]
        private static extern bool IsDebugEnabled();

        [DllImport(Mono.MonoDllName, EntryPoint="mono_debug_il_offset_from_address", CharSet = CharSet.Ansi)]
        private static extern int Debug_GetIlOffsetFromNativeOffset(IntPtr monoMethod, IntPtr monodomain, uint nativeOffset);

        [DllImport(Mono.MonoDllName, EntryPoint="mono_debug_lookup_source_location_by_il", CharSet = CharSet.Ansi)]
        private static extern IntPtr Debug_GetSourceLocation(IntPtr monoMethod, uint ilOffset, IntPtr monoDomain);
        
        [DllImport(Mono.MonoDllName, EntryPoint="mono_debug_free_source_location", CharSet = CharSet.Ansi)]
        private static extern IntPtr Debug_FreeSourceLocation(IntPtr sourceLocation);

        public static bool IsEnabled => IsDebugEnabled();

        static void CheckInput(MonoJitInfo jitInfo)
        {
            if (!jitInfo.MonoMethod.IsValid())
                throw new InvalidOperationException($"The mono method on the {nameof(jitInfo)} must be vald!");
            if (!IsEnabled)
                throw new InvalidOperationException($"Mono debug must be enabled!");
        }

        public static int GetIlOffsetFromNativeOffset(MonoJitInfo jitInfo, uint nativeOffset) {
            CheckInput(jitInfo);
            return Debug_GetIlOffsetFromNativeOffset(jitInfo.MonoMethod.Pointer, Mono.Domain, nativeOffset);
        }

        public static List<MonoIlOffset> GetIlOffsets(MonoJitInfo jitInfo) {
            CheckInput(jitInfo);

            uint codeSize = (uint)jitInfo.CodeSize;
            var list = new List<MonoIlOffset>();

            int lastOffset = -1;
            var ptr = jitInfo.MonoMethod.Pointer;
            var domain = Mono.Domain;
            // TODO: optimize this to use a binary search
            for (uint i = 0; i != codeSize; i++) {
                int result = Debug_GetIlOffsetFromNativeOffset(ptr, domain, i);
                if (result != lastOffset) {
                    lastOffset = result;
                    list.Add(new MonoIlOffset{
                        IlOffset = (uint)result,
                        NativeOffset = i,
                    });
                }
            }

            list.Sort((lhs, rhs) => lhs.NativeOffset.CompareTo(rhs.NativeOffset));
            return list;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        struct MonoSourceLocation_Internal {
            public IntPtr SourceFile;
            public UInt32 Row, Column;
            public UInt32 IlOffset;
        }

        public static List<MonoSourceLocation> GetSourceLocation(MonoJitInfo jitInfo, IEnumerable<uint> ilOffsets)
        {
            CheckInput(jitInfo);
            var ptr = jitInfo.MonoMethod.Pointer;
            var domain = Mono.Domain;

            var sourceLocations = new List<MonoSourceLocation>();
            foreach (var offset in ilOffsets) {
                IntPtr sourceLoc = IntPtr.Zero;
                try {
                    sourceLoc = Debug_GetSourceLocation(ptr, offset, domain);
                    if (sourceLoc == IntPtr.Zero) {
                        sourceLocations.Add(new MonoSourceLocation());
                        continue;
                    }
                    unsafe {
                        var sourceLocPtr = (MonoSourceLocation_Internal*)sourceLoc.ToPointer();
                        var output = new MonoSourceLocation {
                            Column = sourceLocPtr->Column,
                            Row = sourceLocPtr->Row,
                            IlOffset = sourceLocPtr->IlOffset,
                            File = Marshal.PtrToStringAnsi(sourceLocPtr->SourceFile)
                        };
                        sourceLocations.Add(output);
                    }
                }
                finally {
                    if (sourceLoc != IntPtr.Zero)
                        Debug_FreeSourceLocation(sourceLoc);
                }
            }
            return sourceLocations;
        }
    }
}