using System;

namespace AsmExplorer
{
    public struct MonoMethod
    {
        IntPtr m_Ptr;
        internal IntPtr Pointer => m_Ptr;
        internal MonoMethod(IntPtr ptr) { m_Ptr = ptr; }

        public bool IsValid() => m_Ptr != IntPtr.Zero;
    }
}