using System;
using Unity.Collections;

namespace AsmExplorer.Profiler {
    static class NativeContainerExtensions
    {
        public static void TryDispose<T>(this NativeList<T> arr) where T : unmanaged
        {
            if (arr.IsCreated)
                arr.Dispose();
        }

        public static void TryDispose<T>(this NativeArray<T> arr) where T : unmanaged
        {
            if (arr.IsCreated)
                arr.Dispose();
        }
    }
}
