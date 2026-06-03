using ClearVision.Product.Infrastructure.TestData;

// 生成测试点云数据
Console.WriteLine("生成合成点云测试数据...");

var outputDir = "../../../tests/TestData";
Directory.CreateDirectory(outputDir);

// 1. 平面点云
var plane = SyntheticPointCloudGenerator.GeneratePlane(1000, 1.0f, 0.0005f);
SyntheticPointCloudGenerator.SaveToPcd(plane, Path.Combine(outputDir, "synthetic_plane_1000.pcd"));
Console.WriteLine($"✓ 平面点云: {plane.Count} 点");

// 2. 球体点云
var sphere = SyntheticPointCloudGenerator.GenerateSphere(2000, 0.05f, 0.0005f);
SyntheticPointCloudGenerator.SaveToPcd(sphere, Path.Combine(outputDir, "synthetic_sphere_2000.pcd"));
Console.WriteLine($"✓ 球体点云: {sphere.Count} 点");

// 3. 圆柱体点云
var cylinder = SyntheticPointCloudGenerator.GenerateCylinder(1500, 0.03f, 0.1f, 0.0005f);
SyntheticPointCloudGenerator.SaveToPcd(cylinder, Path.Combine(outputDir, "synthetic_cylinder_1500.pcd"));
Console.WriteLine($"✓ 圆柱体点云: {cylinder.Count} 点");

// 4. 立方体点云
var cube = SyntheticPointCloudGenerator.GenerateCube(1200, 0.1f, 0.0005f);
SyntheticPointCloudGenerator.SaveToPcd(cube, Path.Combine(outputDir, "synthetic_cube_1200.pcd"));
Console.WriteLine($"✓ 立方体点云: {cube.Count} 点");

Console.WriteLine($"\n全部完成！文件保存到: {Path.GetFullPath(outputDir)}");
