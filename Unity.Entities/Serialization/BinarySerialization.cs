using System;
#if !NET_DOTS
using System.IO;
using Unity.Assertions;
#endif
using Unity.IO.LowLevel.Unsafe;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.Entities.Serialization
{
    public interface BinaryWriter : IDisposable
    {
        unsafe void WriteBytes(void* data, int bytes);
        long Position { get; set; }
    }

    public static unsafe class BinaryWriterExtensions
    {
        public static void Write(this BinaryWriter writer, byte value)
        {
            writer.WriteBytes(&value, 1);
        }

        public static void Write(this BinaryWriter writer, int value)
        {
            writer.WriteBytes(&value, sizeof(int));
        }

        public static void Write(this BinaryWriter writer, ulong value)
        {
            writer.WriteBytes(&value, sizeof(ulong));
        }

        public static void Write(this BinaryWriter writer, byte[] bytes)
        {
            fixed(byte* p = bytes)
            {
                writer.WriteBytes(p, bytes.Length);
            }
        }

        public static void WriteArray<T>(this BinaryWriter writer, NativeArray<T> data) where T: struct
        {
            writer.WriteBytes(data.GetUnsafeReadOnlyPtr(), data.Length * UnsafeUtility.SizeOf<T>());
        }

        public static void WriteList<T>(this BinaryWriter writer, NativeList<T> data) where T: unmanaged
        {
            writer.WriteBytes(data.GetUnsafePtr(), data.Length * UnsafeUtility.SizeOf<T>());
        }

        public static void WriteList<T>(this BinaryWriter writer, NativeList<T> data, int index, int count) where T: unmanaged
        {
            if (index + count > data.Length)
            {
                throw new ArgumentException("index + count must not go beyond the end of the list");
            }
            var size = UnsafeUtility.SizeOf<T>();
            writer.WriteBytes((byte*)data.GetUnsafePtr() + size*index, count * size);
        }
    }

    public interface BinaryReader : IDisposable
    {
        unsafe void ReadBytes(void* data, int bytes);
        long Position { get; set; }
    }

    public static unsafe class BinaryReaderExtensions
    {
        public static byte ReadByte(this BinaryReader reader)
        {
            byte value;
            reader.ReadBytes(&value, 1);
            return value;
        }

        public static int ReadInt(this BinaryReader reader)
        {
            int value;
            reader.ReadBytes(&value, sizeof(int));
            return value;
        }

        public static ulong ReadULong(this BinaryReader reader)
        {
            ulong value;
            reader.ReadBytes(&value, sizeof(ulong));
            return value;
        }

        public static void ReadBytes(this BinaryReader writer, NativeArray<byte> elements, int count, int offset = 0)
        {
            byte* destination = (byte*)elements.GetUnsafePtr() + offset;
            writer.ReadBytes(destination, count);
        }

        public static void ReadArray<T>(this BinaryReader reader, NativeArray<T> elements, int count) where T: struct
        {
            reader.ReadBytes((byte*)elements.GetUnsafeReadOnlyPtr(), count * UnsafeUtility.SizeOf<T>());
        }
    }

#if !NET_DOTS
    internal unsafe class StreamBinaryReader : BinaryReader
    {
        internal string FilePath { get; }
#if UNITY_EDITOR
        private Stream stream;
        private byte[] buffer;
        public long Position
        {
            get => stream.Position;
            set => stream.Position = value;
        }
#else
        public long Position { get; set; }
#endif

        public StreamBinaryReader(string filePath, long bufferSize = 65536)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentException("The filepath can neither be null nor empty", nameof(filePath));

            FilePath = filePath;
            #if UNITY_EDITOR
            stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            buffer = new byte[bufferSize];
            #else
            Position = 0;
            #endif
        }

        public void Dispose()
        {
            #if UNITY_EDITOR
            stream.Dispose();
            #endif
        }

        public void ReadBytes(void* data, int bytes)
        {
            #if UNITY_EDITOR
            int remaining = bytes;
            int bufferSize = buffer.Length;

            fixed(byte* fixedBuffer = buffer)
            {
                while (remaining != 0)
                {
                    int read = stream.Read(buffer, 0, Math.Min(remaining, bufferSize));
                    remaining -= read;
                    UnsafeUtility.MemCpy(data, fixedBuffer, read);
                    data = (byte*)data + read;
                }
            }
            #else
            var readCmd = new ReadCommand
            {
                Size = bytes, Offset = Position, Buffer = data
            };
            Assert.IsFalse(string.IsNullOrEmpty(FilePath));
#if ENABLE_PROFILER && UNITY_2020_2_OR_NEWER
            // When AsyncReadManagerMetrics are available, mark up the file read for more informative IO metrics.
            // Metrics can be retrieved by AsyncReadManagerMetrics.GetMetrics
            var readHandle = AsyncReadManager.Read(FilePath, &readCmd, 1, subsystem: AssetLoadingSubsystem.EntitiesStreamBinaryReader);
#else
            var readHandle = AsyncReadManager.Read(FilePath, &readCmd, 1);
#endif
            readHandle.JobHandle.Complete();

            if (readHandle.Status != ReadStatus.Complete)
            {
                throw new IOException($"Failed to read from {FilePath}!");
            }
            Position += bytes;
            #endif
        }
    }

    internal unsafe class StreamBinaryWriter : BinaryWriter
    {
        private Stream stream;
        private byte[] buffer;
        public long Position
        {
            get => stream.Position;
            set => stream.Position = value;
        }

        public StreamBinaryWriter(string fileName, int bufferSize = 65536)
        {
            stream = File.Open(fileName, FileMode.Create, FileAccess.Write);
            buffer = new byte[bufferSize];
        }

        public void Dispose()
        {
            stream.Dispose();
        }

        public void WriteBytes(void* data, int bytes)
        {
            int remaining = bytes;
            int bufferSize = buffer.Length;

            fixed (byte* fixedBuffer = buffer)
            {
                while (remaining != 0)
                {
                    int bytesToWrite = Math.Min(remaining, bufferSize);
                    UnsafeUtility.MemCpy(fixedBuffer, data, bytesToWrite);
                    stream.Write(buffer, 0, bytesToWrite);
                    data = (byte*) data + bytesToWrite;
                    remaining -= bytesToWrite;
                }
            }
        }

        public long Length => stream.Length;
    }
#endif

    public unsafe class MemoryBinaryWriter : Entities.Serialization.BinaryWriter
    {
        NativeList<byte> content = new NativeList<byte>(Allocator.Temp);
        public byte* Data => (byte*)content.GetUnsafePtr();
        public int Length => content.Length;
        public long Position { get; set; }

        public void Dispose()
        {
            content.Dispose();
        }

        internal NativeArray<byte> GetContentAsNativeArray() => content.AsArray();

        public void WriteBytes(void* data, int bytes)
        {
            content.ResizeUninitialized((int)Position + bytes);
            UnsafeUtility.MemCpy((byte*)content.GetUnsafePtr() + (int)Position, data, bytes);
            Position += bytes;
        }
    }

    public unsafe class MemoryBinaryReader : BinaryReader
    {
        readonly byte* content;
        readonly long length;

        public long Position { get; set; }

        [Obsolete("MemoryBinaryReader(byte* content) will be removed. Please use the constructor that also takes the length of the buffer. (RemovedAfter 2021-04-10)")]
        public MemoryBinaryReader(byte* content)
        {
            this.content = content;
            this.length = long.MaxValue;
            Position = 0L;
        }

        public MemoryBinaryReader(byte* content, long length)
        {
            this.content = content;
            this.length = length;
            Position = 0L;
        }

        public void Dispose()
        {
        }

        public void ReadBytes(void* data, int bytes)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (Position + bytes > length)
                throw new ArgumentException("ReadBytes reads beyond end of memory block");
#endif
            UnsafeUtility.MemCpy(data, content + Position, bytes);
            Position += bytes;
        }
        public static explicit operator BurstableMemoryBinaryReader(MemoryBinaryReader src)
        {
            return new BurstableMemoryBinaryReader {content = src.content, length = src.length, Position = src.Position};
        }
    }

    [BurstCompatible]
    public unsafe struct BurstableMemoryBinaryReader : BinaryReader
    {
        internal byte* content;
        internal long length;

        public long Position { get; set; }

        public BurstableMemoryBinaryReader(byte* content, long length)
        {
            this.content = content;
            this.length = length;
            Position = 0L;
        }

        public void Dispose()
        {
        }

        public byte ReadByte()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (Position + sizeof(byte) > length)
                throw new ArgumentException("ReadByte reads beyond end of memory block");
#endif
            var res = *(content + Position);
            Position += sizeof(byte);
            return res;
        }

        public int ReadInt()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (Position + sizeof(int) > length)
                throw new ArgumentException("ReadInt reads beyond end of memory block");
#endif
            var res = *(int*) (content + Position);
            Position += sizeof(int);
            return res;
        }

        public ulong ReadULong()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (Position + sizeof(ulong) > length)
                throw new ArgumentException("ReadULong reads beyond end of memory block");
#endif
            var res = *(ulong*) (content + Position);
            Position += sizeof(ulong);
            return res;
        }

        public void ReadBytes(void* data, int bytes)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (Position + bytes > length)
                throw new ArgumentException("ReadBytes reads beyond end of memory block");
#endif
            UnsafeUtility.MemCpy(data, content + Position, bytes);
            Position += bytes;
        }
    }
}
