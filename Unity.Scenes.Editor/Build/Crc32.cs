using System;

namespace Unity.Entities.Content
{
    // Standard IEEE 802.3 CRC-32 (polynomial 0xEDB88320, reflected). Produces values identical
    // to System.IO.Hashing.Crc32, zlib's crc32(), and the CRC used in PNG/gzip/ZIP/Ethernet.
    // Used by RemoteContentCatalogBuildUtility to populate RemoteContentLocation.Crc.
    internal static class Crc32
    {
        static readonly uint[] s_Table = CreateTable();

        static uint[] CreateTable()
        {
            const uint poly = 0xEDB88320u;
            var t = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int j = 0; j < 8; j++)
                    c = (c & 1) != 0 ? (c >> 1) ^ poly : (c >> 1);
                t[i] = c;
            }
            return t;
        }

        public static uint Compute(ReadOnlySpan<byte> data)
        {
            uint crc = 0xFFFFFFFFu;
            for (int i = 0; i < data.Length; i++)
                crc = (crc >> 8) ^ s_Table[(crc ^ data[i]) & 0xFF];
            return ~crc;
        }
    }
}
