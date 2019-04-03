using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Unity.Collections.LowLevel.Unsafe;
using System.Collections.Generic;

[assembly: InternalsVisibleTo("Unity.Entities.Tests")]

namespace Unity.Entities
{
    public static class FastEquality
    {
        internal static Layout[] CreateLayout(Type type)
        {
            var begin = 0;
            var end = 0;

            var layouts = new List<Layout>();

            CreateLayoutRecurse(type, 0, layouts, ref begin, ref end);

            if (begin != end)
                layouts.Add(new Layout {offset = begin, count = end - begin, Aligned4 = false});

            var layoutsArray = layouts.ToArray();

            for (var i = 0; i != layoutsArray.Length; i++)
                if (layoutsArray[i].count % 4 == 0 && layoutsArray[i].offset % 4 == 0)
                {
                    layoutsArray[i].count /= 4;
                    layoutsArray[i].Aligned4 = true;
                }

            return layoutsArray;
        }

        public struct Layout
        {
            public int offset;
            public int count;
            public bool Aligned4;

            public override string ToString()
            {
                return $"offset: {offset} count: {count} Aligned4: {Aligned4}";
            }
        }

        private unsafe struct PointerSize
        {
#pragma warning disable 0169 // "never used" warning
            private void* pter;
#pragma warning restore 0169
        }

        private static void CreateLayoutRecurse(Type type, int baseOffset, List<Layout> layouts, ref int begin,
            ref int end)
        {
            var fields = type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            foreach (var field in fields)
            {
                var offset = baseOffset + UnsafeUtility.GetFieldOffset(field);

                if (field.FieldType.IsPrimitive || field.FieldType.IsPointer || field.FieldType.IsClass)
                {
                    var sizeOf = -1;
                    if (field.FieldType.IsPointer || field.FieldType.IsClass)
                        sizeOf = UnsafeUtility.SizeOf<PointerSize>();
                    else
                        sizeOf = UnsafeUtility.SizeOf(field.FieldType);

                    if (end != offset)
                    {
                        layouts.Add(new Layout {offset = begin, count = end - begin, Aligned4 = false});
                        begin = offset;
                        end = offset + sizeOf;
                    }
                    else
                    {
                        end += sizeOf;
                    }
                }
                else
                {
                    CreateLayoutRecurse(field.FieldType, offset, layouts, ref begin, ref end);
                }
            }
        }

        //@TODO: Encode type in hashcode...

        private const int FNV_32_PRIME = 0x01000193;

        public static unsafe int GetHashCode<T>(T lhs, Layout[] layout) where T : struct
        {
            return GetHashCode(UnsafeUtility.AddressOf(ref lhs), layout);
        }

        public static unsafe int GetHashCode<T>(ref T lhs, Layout[] layout) where T : struct
        {
            return GetHashCode(UnsafeUtility.AddressOf(ref lhs), layout);
        }

        public static unsafe int GetHashCode(void* dataPtr, Layout[] layout)
        {
            var data = (byte*) dataPtr;
            uint hash = 0;

            for (var k = 0; k != layout.Length; k++)
                if (layout[k].Aligned4)
                {
                    var dataInt = (uint*) (data + layout[k].offset);
                    var count = layout[k].count;
                    for (var i = 0; i != count; i++)
                    {
                        hash *= FNV_32_PRIME;
                        hash ^= dataInt[i];
                    }
                }
                else
                {
                    var dataByte = data + layout[k].offset;
                    var count = layout[k].count;
                    for (var i = 0; i != count; i++)
                    {
                        hash *= FNV_32_PRIME;
                        hash ^= dataByte[i];
                    }
                }

            return (int) hash;
        }

        public static unsafe bool Equals<T>(T lhs, T rhs, Layout[] layout) where T : struct
        {
            return Equals(UnsafeUtility.AddressOf(ref lhs), UnsafeUtility.AddressOf(ref rhs), layout);
        }

        public static unsafe bool Equals<T>(ref T lhs, ref T rhs, Layout[] layout) where T : struct
        {
            return Equals(UnsafeUtility.AddressOf(ref lhs), UnsafeUtility.AddressOf(ref rhs), layout);
        }

        public static unsafe bool Equals(void* lhsPtr, void* rhsPtr, Layout[] layout)
        {
            var lhs = (byte*) lhsPtr;
            var rhs = (byte*) rhsPtr;

            var same = true;

            for (var k = 0; k != layout.Length; k++)
                if (layout[k].Aligned4)
                {
                    var offset = layout[k].offset;
                    var lhsInt = (uint*) (lhs + offset);
                    var rhsInt = (uint*) (rhs + offset);
                    var count = layout[k].count;
                    for (var i = 0; i != count; i++)
                        same &= lhsInt[i] == rhsInt[i];
                }
                else
                {
                    var offset = layout[k].offset;
                    var lhsByte = lhs + offset;
                    var rhsByte = rhs + offset;
                    var count = layout[k].count;
                    for (var i = 0; i != count; i++)
                        same &= lhsByte[i] == rhsByte[i];
                }

            return same;
        }
    }
}
