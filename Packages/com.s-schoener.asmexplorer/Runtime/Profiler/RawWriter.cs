using System;
using System.IO;
using Unity.Collections.LowLevel.Unsafe;

namespace AsmExplorer.Profiler {
    struct RawWriter : IDisposable
    {
        BinaryWriter m_Writer;
        byte[] m_Buffer;

        public RawWriter(BinaryWriter writer, int bufferSize = 65536)
        {
            m_Writer = writer;
            m_Buffer = new byte[bufferSize];
        }

        public void Dispose() => m_Writer.Dispose();

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
                    m_Writer.Write(m_Buffer, 0, bytesToWrite);
                    data = (byte*) data + bytesToWrite;
                    remaining -= bytesToWrite;
                }
            }
        }
    }
}
