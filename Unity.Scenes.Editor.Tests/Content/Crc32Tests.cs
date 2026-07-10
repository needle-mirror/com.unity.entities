using System.Text;
using NUnit.Framework;
using Unity.Entities.Content;

namespace Unity.Scenes.Editor.Tests.Content
{
    // Expected values captured from System.IO.Hashing.Crc32 (.NET 8) to verify our impl
    // matches the IEEE 802.3 CRC-32 used by PNG/gzip/ZIP/Ethernet.
    //
    // To regenerate: drop the Program.cs and .csproj contents shown below into a scratch
    // folder, run `dotnet run`, paste the output into the [TestCase] attributes.
    //
    //   // ----- Program.cs -----
    //   using System;
    //   using System.IO.Hashing;
    //   using System.Text;
    //
    //   static void PrintStringCase(string input)
    //   {
    //       byte[] bytes = Encoding.UTF8.GetBytes(input);
    //       uint crc = Crc32.HashToUInt32(bytes);
    //       string escaped = input.Replace("\\", "\\\\").Replace("\"", "\\\"");
    //       Console.WriteLine($"        [TestCase(\"{escaped}\", 0x{crc:X8}u)]");
    //   }
    //
    //   static void PrintSequentialCase(int length)
    //   {
    //       byte[] bytes = new byte[length];
    //       for (int i = 0; i < length; i++) bytes[i] = (byte)i;
    //       uint crc = Crc32.HashToUInt32(bytes);
    //       Console.WriteLine($"        [TestCase({length}, 0x{crc:X8}u)]");
    //   }
    //
    //   PrintStringCase("");
    //   PrintStringCase("a");
    //   PrintStringCase("abc");
    //   PrintStringCase("123456789");
    //   PrintStringCase("The quick brown fox jumps over the lazy dog");
    //   PrintStringCase("Hello, World!");
    //   PrintStringCase("abcdefghijklmnopqrstuvwxyz");
    //   PrintStringCase("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789");
    //
    //   int[] lengths = { 0, 1, 2, 3, 4, 7, 8, 15, 16, 17, 31, 32, 63, 64, 100, 255, 256, 257, 511, 512, 1000, 1024, 4096, 65536 };
    //   foreach (var n in lengths) PrintSequentialCase(n);
    //
    //   // ----- crc32gen.csproj -----
    //   // <Project Sdk="Microsoft.NET.Sdk">
    //   //   <PropertyGroup>
    //   //     <OutputType>Exe</OutputType>
    //   //     <TargetFramework>net8.0</TargetFramework>
    //   //   </PropertyGroup>
    //   //   <ItemGroup>
    //   //     <PackageReference Include="System.IO.Hashing" Version="8.0.0" />
    //   //   </ItemGroup>
    //   // </Project>
    [TestFixture]
    class Crc32Tests
    {
        [TestCase("", 0x00000000u)]
        [TestCase("a", 0xE8B7BE43u)]
        [TestCase("abc", 0x352441C2u)]
        [TestCase("123456789", 0xCBF43926u)] // canonical CRC-32 check value
        [TestCase("The quick brown fox jumps over the lazy dog", 0x414FA339u)]
        [TestCase("Hello, World!", 0xEC4AC3D0u)]
        [TestCase("abcdefghijklmnopqrstuvwxyz", 0x4C2750BDu)]
        [TestCase("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789", 0x1FC2E6D2u)]
        public void Crc32_MatchesReference_ForString(string input, uint expected)
        {
            Assert.AreEqual(expected, Crc32.Compute(Encoding.UTF8.GetBytes(input)));
        }

        // Sequential-byte buffers where input[i] = (byte)i. Exercises various lengths around
        // power-of-two and chunk boundaries.
        [TestCase(0, 0x00000000u)]
        [TestCase(1, 0xD202EF8Du)]
        [TestCase(2, 0x36DE2269u)]
        [TestCase(3, 0x0854897Fu)]
        [TestCase(4, 0x8BB98613u)]
        [TestCase(7, 0xAD5809F9u)]
        [TestCase(8, 0x88AA689Fu)]
        [TestCase(15, 0xA06C675Eu)]
        [TestCase(16, 0xCECEE288u)]
        [TestCase(17, 0x2C183A19u)]
        [TestCase(31, 0x4D786D77u)]
        [TestCase(32, 0x91267E8Au)]
        [TestCase(63, 0xDBDEA683u)]
        [TestCase(64, 0x100ECE8Cu)]
        [TestCase(100, 0x58C932F5u)]
        [TestCase(255, 0xD32F9BA0u)]
        [TestCase(256, 0x29058C73u)]
        [TestCase(257, 0x1B27CA87u)]
        [TestCase(511, 0x023E6488u)]
        [TestCase(512, 0x1C613576u)]
        [TestCase(1000, 0x74E3FB41u)]
        [TestCase(1024, 0xB70B4C26u)]
        [TestCase(4096, 0xA2912082u)]
        [TestCase(65536, 0xB11DE6A1u)]
        public void Crc32_MatchesReference_ForSequentialBytes(int length, uint expected)
        {
            var bytes = new byte[length];
            for (int i = 0; i < length; i++)
                bytes[i] = (byte)i;
            Assert.AreEqual(expected, Crc32.Compute(bytes));
        }
    }
}
