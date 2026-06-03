using System.Numerics;
using System.Text;

namespace ClearVision.Product.Infrastructure.TestData;

/// <summary>
/// 合成3D点云生成器 - 用于阶段2测试
/// </summary>
public static class SyntheticPointCloudGenerator
{
    private static readonly Random Random = new();

    /// <summary>
    /// 生成平面点云
    /// </summary>
    public static List<Vector3> GeneratePlane(int count, float size, float noise)
    {
        var points = new List<Vector3>(count);
        for (int i = 0; i < count; i++)
        {
            float x = (float)(Random.NextDouble() - 0.5) * size;
            float y = (float)(Random.NextDouble() - 0.5) * size;
            float z = RandomNoise(noise);
            points.Add(new Vector3(x, y, z));
        }
        return points;
    }

    /// <summary>
    /// 生成球体点云
    /// </summary>
    public static List<Vector3> GenerateSphere(int count, float radius, float noise)
    {
        var points = new List<Vector3>(count);
        for (int i = 0; i < count; i++)
        {
            float theta = (float)(Random.NextDouble() * Math.PI * 2);
            float phi = (float)(Math.Acos(2 * Random.NextDouble() - 1));
            float r = radius + RandomNoise(noise);

            float x = r * (float)Math.Sin(phi) * (float)Math.Cos(theta);
            float y = r * (float)Math.Sin(phi) * (float)Math.Sin(theta);
            float z = r * (float)Math.Cos(phi);

            points.Add(new Vector3(x, y, z));
        }
        return points;
    }

    /// <summary>
    /// 生成圆柱体点云
    /// </summary>
    public static List<Vector3> GenerateCylinder(int count, float radius, float height, float noise)
    {
        var points = new List<Vector3>(count);
        for (int i = 0; i < count; i++)
        {
            float theta = (float)(Random.NextDouble() * Math.PI * 2);
            float r = radius + RandomNoise(noise);
            float h = (float)(Random.NextDouble() - 0.5) * height;

            float x = r * (float)Math.Cos(theta);
            float y = r * (float)Math.Sin(theta);
            float z = h + RandomNoise(noise);

            points.Add(new Vector3(x, y, z));
        }
        return points;
    }

    /// <summary>
    /// 生成立方体点云
    /// </summary>
    public static List<Vector3> GenerateCube(int count, float size, float noise)
    {
        var points = new List<Vector3>(count);
        float halfSize = size / 2;

        // 在6个面上均匀采样
        int pointsPerFace = count / 6;

        for (int face = 0; face < 6; face++)
        {
            for (int i = 0; i < pointsPerFace; i++)
            {
                float u = (float)(Random.NextDouble() - 0.5) * size;
                float v = (float)(Random.NextDouble() - 0.5) * size;
                float n = RandomNoise(noise);

                Vector3 point = face switch
                {
                    0 => new Vector3(halfSize + n, u, v),    // +X
                    1 => new Vector3(-halfSize + n, u, v),   // -X
                    2 => new Vector3(u, halfSize + n, v),    // +Y
                    3 => new Vector3(u, -halfSize + n, v),   // -Y
                    4 => new Vector3(u, v, halfSize + n),    // +Z
                    5 => new Vector3(u, v, -halfSize + n),   // -Z
                    _ => Vector3.Zero
                };

                points.Add(point);
            }
        }

        // 补充剩余点
        while (points.Count < count)
        {
            points.Add(new Vector3(
                (float)(Random.NextDouble() - 0.5) * size + RandomNoise(noise),
                (float)(Random.NextDouble() - 0.5) * size + RandomNoise(noise),
                (float)(Random.NextDouble() - 0.5) * size + RandomNoise(noise)
            ));
        }

        return points;
    }

    /// <summary>
    /// 保存点云为PCD格式
    /// </summary>
    public static void SaveToPcd(List<Vector3> points, string filePath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# .PCD v0.7 - Point Cloud Data file format");
        sb.AppendLine("VERSION 0.7");
        sb.AppendLine("FIELDS x y z");
        sb.AppendLine("SIZE 4 4 4");
        sb.AppendLine("TYPE F F F");
        sb.AppendLine("COUNT 1 1 1");
        sb.AppendLine($"WIDTH {points.Count}");
        sb.AppendLine("HEIGHT 1");
        sb.AppendLine("VIEWPOINT 0 0 0 1 0 0 0");
        sb.AppendLine($"POINTS {points.Count}");
        sb.AppendLine("DATA ascii");

        foreach (var p in points)
        {
            sb.AppendLine($"{p.X:F6} {p.Y:F6} {p.Z:F6}");
        }

        File.WriteAllText(filePath, sb.ToString());
    }

    /// <summary>
    /// 生成高斯噪声
    /// </summary>
    private static float RandomNoise(float amplitude)
    {
        if (amplitude <= 0)
            return 0;

        // Box-Muller变换生成近似高斯噪声
        double u1 = 1.0 - Random.NextDouble();
        double u2 = 1.0 - Random.NextDouble();
        double randStdNormal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);

        return (float)(randStdNormal * amplitude);
    }
}
