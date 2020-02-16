using System;
using System.IO;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace AsmExplorer.Profiler {
    struct RawWriter
    {
        Stream m_Stream;
        byte[] m_Buffer;

        public RawWriter(Stream stream, int bufferSize = 65536)
        {
            m_Stream = stream;
            m_Buffer = new byte[bufferSize];
        }

        public unsafe void WriteBytes(void* data, int bytes)
        {
            int remaining = bytes;
            int bufferSize = m_Buffer.Length;

            fixed (byte* fixedBuffer = m_Buffer)
            {
                while (remaining != 0)
                {
                    int bytesToWrite = Math.Min(remaining, bufferSize);
                    UnsafeUtility.MemCpy(fixedBuffer, data, bytesToWrite);
                    m_Stream.Write(m_Buffer, 0, bytesToWrite);
                    data = (byte*) data + bytesToWrite;
                    remaining -= bytesToWrite;
                }
            }
        }

        public unsafe void Write<T>(T* data) where T : unmanaged => WriteBytes(data, sizeof(T));

        public unsafe void WriteArray<T>(NativeArray<T> arr) where T : unmanaged => WriteBytes(arr.GetUnsafeReadOnlyPtr(), arr.Length * sizeof(T));
    }
}
