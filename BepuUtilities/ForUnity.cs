using System.Runtime.CompilerServices;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Collections.Generic;

namespace System.Runtime.InteropServices
{
    public static class FNativeMemory
    {
        public static unsafe void* AlignedAlloc(nuint byteCount, nuint alignment)
        {
#if UNITY
            UnsafeUtility.Alloc();
#elif NET5_0_OR_GREATER
            return NativeMemory.AlignedAlloc(byteCount, alignment);
#else

            return FallbackAllocator.Alloc((int)byteCount, (int)alignment);
#endif
        }

        public static unsafe void AlignedFree(void* ptr)
        {
#if UNITY
            UnsafeUtility.Free();
#elif NET5_0_OR_GREATER
            NativeMemory.AlignedFree(ptr);
#else
            FallbackAllocator.Free(ptr);
#endif
        }
    }

    internal static class FallbackAllocator
    {
        private static Dictionary<IntPtr, IntPtr> _alignmentMapping = new();

        public static unsafe void* Alloc(int count)
        {
            if (count == 0)
            {
                return null;
            }
            return (void*)Marshal.AllocHGlobal((IntPtr)count);
        }

        public static unsafe void* Alloc(int count, int alignment)
        {
            byte* ptr = (byte*)(void*)Marshal.AllocHGlobal(count + alignment);
            long num = (long)ptr;
            long num2 = num % alignment;
            long num3 = alignment - num2;
            if (num2 != 0L)
            {
                lock (_alignmentMapping)
                {
                    _alignmentMapping.Add(new IntPtr(ptr + num3), new IntPtr(ptr));
                }
                return ptr + num3;
            }
            return ptr;
        }

        public static unsafe void Free(void* ptr)
        {
            if (_alignmentMapping.Count > 0)
            {
                lock (_alignmentMapping)
                {
                    IntPtr value;
                    if (_alignmentMapping.Count > 0 && _alignmentMapping.TryGetValue(new IntPtr(ptr), out value))
                    {
                        ptr = value.ToPointer();
                    }
                }
            }
            Marshal.FreeHGlobal((IntPtr)ptr);
        }
    }
}

namespace System.Numerics
{
    public static class FBitOperations
    {
        private static ReadOnlySpan<byte> Log2DeBruijn => // 32
        [
            00,
            09,
            01,
            10,
            13,
            21,
            02,
            29,
            11,
            14,
            16,
            18,
            22,
            25,
            03,
            30,
            08,
            12,
            20,
            28,
            15,
            17,
            24,
            07,
            19,
            27,
            23,
            06,
            26,
            05,
            04,
            31
        ];
        /// <summary>
        /// Count the number of leading zero bits in a mask.
        /// Similar in behavior to the x86 instruction LZCNT.
        /// </summary>
        /// <param name="value">The value.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int LeadingZeroCount(uint value)
        {
#if NET8_0_OR_GREATER
            return BitOperations.LeadingZeroCount(value);
#else
            return LeadingZeroCount_SoftwareFallback(value);
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int LeadingZeroCount_SoftwareFallback(uint value)
        {
            // Unguarded fallback contract is 0->31, BSR contract is 0->undefined
            if (value == 0)
            {
                return 32;
            }

            return 31 ^ Log2SoftwareFallback(value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Log2(uint value)
        {
#if NET8_0_OR_GREATER
            return BitOperations.Log2(value);
#else
            return Log2_SoftwareFallback(value);
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Log2_SoftwareFallback(uint value)
        {
            // The 0->0 contract is fulfilled by setting the LSB to 1.
            // Log(1) is 0, and setting the LSB for values > 1 does not change the log2 result.
            value |= 1;

            // Fallback contract is 0->0
            return Log2SoftwareFallback(value);
        }

        /// <summary>Round the given integral value up to a power of 2.</summary>
        /// <param name="value">The value.</param>
        /// <returns>
        /// The smallest power of 2 which is greater than or equal to <paramref name="value"/>.
        /// If <paramref name="value"/> is 0 or the result overflows, returns 0.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint RoundUpToPowerOf2(uint value)
        {
#if NET8_0_OR_GREATER
            return BitOperations.RoundUpToPowerOf2(value);
#else
            return RoundUpToPowerOf2_SoftwareFallback(value);
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint RoundUpToPowerOf2_SoftwareFallback(uint value)
        {
            // Based on https://graphics.stanford.edu/~seander/bithacks.html#RoundUpPowerOf2
            --value;
            value |= value >> 1;
            value |= value >> 2;
            value |= value >> 4;
            value |= value >> 8;
            value |= value >> 16;
            return value + 1;
        }

        /// <summary>
        /// Round the given integral value up to a power of 2.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <returns>
        /// The smallest power of 2 which is greater than or equal to <paramref name="value"/>.
        /// If <paramref name="value"/> is 0 or the result overflows, returns 0.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong RoundUpToPowerOf2(ulong value)
        {
#if NET8_0_OR_GREATER
            return BitOperations.RoundUpToPowerOf2(value);
#else
            return RoundUpToPowerOf2_SoftwareFallback(value);
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong RoundUpToPowerOf2_SoftwareFallback(ulong value)
        {
            // Based on https://graphics.stanford.edu/~seander/bithacks.html#RoundUpPowerOf2
            --value;
            value |= value >> 1;
            value |= value >> 2;
            value |= value >> 4;
            value |= value >> 8;
            value |= value >> 16;
            value |= value >> 32;
            return value + 1;
        }

        /// <summary>
        /// Returns the integer (floor) log of the specified value, base 2.
        /// Note that by convention, input value 0 returns 0 since Log(0) is undefined.
        /// Does not directly use any hardware intrinsics, nor does it incur branching.
        /// </summary>
        /// <param name="value">The value.</param>
        private static int Log2SoftwareFallback(uint value)
        {
            // No AggressiveInlining due to large method size
            // Has conventional contract 0->0 (Log(0) is undefined)

            // Fill trailing zeros with ones, eg 00010010 becomes 00011111
            value |= value >> 01;
            value |= value >> 02;
            value |= value >> 04;
            value |= value >> 08;
            value |= value >> 16;

            // uint.MaxValue >> 27 is always in range [0 - 31] so we use Unsafe.AddByteOffset to avoid bounds check
            return Unsafe.AddByteOffset(
                // Using deBruijn sequence, k=2, n=5 (2^5=32) : 0b_0000_0111_1100_0100_1010_1100_1101_1101u
                ref MemoryMarshal.GetReference(Log2DeBruijn),
                // uint|long -> IntPtr cast on 32-bit platforms does expensive overflow checks not needed here
                (IntPtr)(int)((value * 0x07C4ACDDu) >> 27));
        }
    }
}

namespace System.Numerics
{
    public static class FVector
    {
        /// <summary>Computes the floor of each element in a vector.</summary>
        /// <param name="value">The vector that will have its floor computed.</param>
        /// <returns>A vector whose elements are the floor of the elements in <paramref name="value" />.</returns>
        /// <seealso cref="MathF.Floor(float)" />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<float> FloorCore(Vector<float> value)
        {
            Unsafe.SkipInit(out Vector<float> result);

            for (int index = 0; index < Vector<float>.Count; index++)
            {
                float element = MathF.Floor(value.GetElementUnsafe(index));
                result.SetElementUnsafe(index, element);
            }

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static T GetElementUnsafe<T>(in this Vector<T> vector, int index)
            where T : struct
        {
            Debug.Assert((index >= 0) && (index < Vector<T>.Count));
            ref T address = ref Unsafe.As<Vector<T>, T>(ref Unsafe.AsRef(in vector));
            return Unsafe.Add(ref address, index);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void SetElementUnsafe<T>(in this Vector<T> vector, int index, T value)
            where T : struct
        {
            Debug.Assert((index >= 0) && (index < Vector<T>.Count));
            ref T address = ref Unsafe.As<Vector<T>, T>(ref Unsafe.AsRef(in vector));
            Unsafe.Add(ref address, index) = value;
        }

#if NET8_0_OR_GREATER
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
        public static Vector<float> Floor(Vector<float> x)
        {
#if NET8_0_OR_GREATER
            return Vector.Floor(x);
#else
            return FloorCore(x);
#endif
        }
    }
}