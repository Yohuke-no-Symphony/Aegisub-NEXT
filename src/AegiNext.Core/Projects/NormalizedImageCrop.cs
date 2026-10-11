namespace AegiNext.Core.Projects;

/// <summary>源图像归一化矩形，所有分量位于零至一范围且矩形非空。</summary>
public sealed record NormalizedImageCrop(double X = 0, double Y = 0, double Width = 1, double Height = 1);
