using System;
using System.Numerics;
using System.Runtime.Intrinsics;
using BepuUtilities;
using DemoContentLoader;
using DemoUtilities;
using OpenTK;

namespace Demos
{
    class Program
    {
        static void Main()
        {
            //Test();

            var window = new Window("pretty cool multicolored window",
                new Int2((int)(DisplayDevice.Default.Width * 0.75f), (int)(DisplayDevice.Default.Height * 0.75f)), WindowMode.Windowed);
            var loop = new GameLoop(window);
            ContentArchive content;
            using (var stream = typeof(Program).Assembly.GetManifestResourceStream("Demos.Demos.contentarchive"))
            {
                content = ContentArchive.Load(stream);
            }
            //HeadlessTest.Test<ShapePileTestDemo>(content, 4, 32, 512);
            var demo = new DemoHarness(loop, content);
            loop.Run(demo);
            loop.Dispose();
            window.Dispose();
        }

        static void Test()
        {
            // 测试通过
            const int NUM = 1000000;
            for (int i = 0; i < NUM; i++)
            {
                System.Diagnostics.Debug.Assert(FBitOperations.LeadingZeroCount_SoftwareFallback((uint)i) == BitOperations.LeadingZeroCount((uint)i), $"{i} LeadingZeroCount");
                System.Diagnostics.Debug.Assert(FBitOperations.Log2_SoftwareFallback((uint)i) == BitOperations.Log2((uint)i), $"{i} Log2");
                System.Diagnostics.Debug.Assert(FBitOperations.RoundUpToPowerOf2_SoftwareFallback((uint)i) == BitOperations.RoundUpToPowerOf2((uint)i), $"{i} RoundUpToPowerOf2 uint");
                System.Diagnostics.Debug.Assert(FBitOperations.RoundUpToPowerOf2_SoftwareFallback((ulong)i) == BitOperations.RoundUpToPowerOf2((ulong)i), $"{i} LeadingZeroCount ulong");
            }

            var rnd = new Random();
            Span<float> span = stackalloc float[Vector<float>.Count];
            for (int i = 0; i < span.Length; i++)
            {
                span[i] = rnd.NextSingle();
            }
            var vectors = new Vector<float>(span);
            System.Diagnostics.Debug.Assert(FVector.FloorCore(vectors) == Vector.Floor(vectors), $"{vectors} Vector.Floor");
        }
    }
}