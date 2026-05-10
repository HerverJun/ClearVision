using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Configs;
using Acme.Product.Core.Cameras;
using OpenCvSharp;
using System;
using System.Runtime.InteropServices;

namespace Acme.Product.Benchmarks
{
    [MemoryDiagnoser]
    [SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 5)]
    public class LOH_GC_Benchmark
    {
        private Mat _dummyImage;

        [GlobalSetup]
        public void Setup()
        {
            // Simulate an industrial camera typical resolution (e.g., 2048 x 1536, 1 channel 8-bit)
            _dummyImage = new Mat(1536, 2048, MatType.CV_8UC1);
            // Fill with random bytes to prevent trivial sorts
            _dummyImage.Randu(new Scalar(0), new Scalar(255));
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            _dummyImage?.Dispose();
        }

        // ============================================
        // 1. The OLD implementation (Allocates new byte[])
        // ============================================
        [Benchmark(Baseline = true)]
        public double Old_CalculateMedian()
        {
            // 将图像数据展平并排序
            var data = new byte[_dummyImage.Total()];
            Marshal.Copy(_dummyImage.Data, data, 0, data.Length);
            Array.Sort(data);

            if (data.Length % 2 == 0)
            {
                return (data[data.Length / 2 - 1] + data[data.Length / 2]) / 2.0;
            }
            else
            {
                return data[data.Length / 2];
            }
        }

        // ============================================
        // 2. The NEW implementation (ArrayPool)
        // ============================================
        [Benchmark]
        public double New_CalculateMedian_ArrayPool()
        {
            int length = (int)_dummyImage.Total();
            if (length == 0) return 0;

            // 使用内存池优化巨量像素分配，缓解 LOH 和 GC 暂停
            var pooledData = System.Buffers.ArrayPool<byte>.Shared.Rent(length);
            try
            {
                Marshal.Copy(_dummyImage.Data, pooledData, 0, length);
                // 仅对有效长度进行排序，忽略内存池分配的末尾冗余数据
                Array.Sort(pooledData, 0, length);

                if (length % 2 == 0)
                {
                    return (pooledData[length / 2 - 1] + pooledData[length / 2]) / 2.0;
                }
                else
                {
                    return pooledData[length / 2];
                }
            }
            finally
            {
                System.Buffers.ArrayPool<byte>.Shared.Return(pooledData);
            }
        }
    }

    [MemoryDiagnoser]
    [SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 5)]
    public class CameraFrameMetadataBenchmark
    {
        private byte[] _encodedFrame = Array.Empty<byte>();

        [GlobalSetup]
        public void Setup()
        {
            using var image = new Mat(1536, 2048, MatType.CV_8UC1);
            image.Randu(new Scalar(0), new Scalar(255));
            _encodedFrame = image.ToBytes(".jpg", new[] { (int)ImwriteFlags.JpegQuality, 85 });
        }

        [Benchmark(Baseline = true)]
        public CameraStreamFrame DecodeDimensionsOnPublish()
        {
            using var decoded = Cv2.ImDecode(_encodedFrame, ImreadModes.Unchanged);
            if (decoded.Empty())
            {
                throw new InvalidOperationException("Unable to decode camera frame.");
            }

            return new CameraStreamFrame(
                "bench-camera",
                _encodedFrame,
                "image/jpeg",
                decoded.Width,
                decoded.Height,
                0,
                DateTime.UtcNow,
                CameraTimestampNs: 123,
                DeviceFrameCounter: 456,
                Stride: 2048);
        }

        [Benchmark]
        public CameraStreamFrame UseCameraMetadataOnPublish()
        {
            return new CameraStreamFrame(
                "bench-camera",
                _encodedFrame,
                "image/jpeg",
                2048,
                1536,
                0,
                DateTime.UtcNow,
                CameraTimestampNs: 123,
                DeviceFrameCounter: 456,
                Stride: 2048);
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, DefaultConfig.Instance);
        }
    }
}
