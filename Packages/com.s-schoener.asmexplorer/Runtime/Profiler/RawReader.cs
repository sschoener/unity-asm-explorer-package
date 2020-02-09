using System;
using System.IO;
using Unity.Collections.LowLevel.Unsafe;

namespace AsmExplorer.Profiler {
    struct RawReader
    {
        Stream m_Stream;
        byte[] m_Buffer;

        public RawReader(Stream stream, int bufferSize = 65536)
        {
            m_Stream = stream;
            m_Buffer = new byte[bufferSize];
        }

        public unsafe void ReadBytes(void* data, int bytes)
        {
            int remaining = bytes;
            int bufferSize = m_Buffer.Length;

            fixed (byte* fixedBuffer = m_Buffer)
            {
                while (remaining != 0)
                {
                    int read = m_Stream.Read(m_Buffer, 0, Math.Min(remaining, bufferSize));
                    if (read <= 0)
                        throw new EndOfStreamException($"Tried to read {remaining} more bytes, but there are none");
                    remaining -= read;
                    UnsafeUtility.MemCpy(data, fixedBuffer, read);
                    data = (byte*) data + read;
                }
            }
        }

        public unsafe void Read<T>(T* data) where T : unmanaged => ReadBytes(data, sizeof(T));
    }
}
