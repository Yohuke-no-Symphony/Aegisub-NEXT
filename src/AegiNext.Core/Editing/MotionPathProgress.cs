using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Core.Editing;

/// <summary>运动路径在没有进度轨道时的自然内容时钟，以及等价的线性关键帧轨道。</summary>
public static class MotionPathProgress
{
    /// <summary>按路径内容时长计算自然进度，并在路径前后保持起点和终点。</summary>
    public static double EvaluateNatural(MotionPath motion, MediaTime contentTime)
    {
        ArgumentNullException.ThrowIfNull(motion);
        if (motion.Duration <= MediaTime.Zero)
        {
            throw new ArgumentException("The motion path duration must be positive.", nameof(motion));
        }
        return Math.Clamp(((double)contentTime.Numerator / contentTime.Denominator) /
            ((double)motion.Duration.Numerator / motion.Duration.Denominator), 0, 1);
    }

    /// <summary>创建与自然进度等价的端点轨道，由图层时间语义裁剪到当前内容区间。</summary>
    public static AnimationTrack CreateNaturalTrack(MotionPath motion)
    {
        ArgumentNullException.ThrowIfNull(motion);
        if (motion.Duration <= MediaTime.Zero)
        {
            throw new ArgumentException("The motion path duration must be positive.", nameof(motion));
        }
        return new(AnimationProperty.PATH_PROGRESS, [new(MediaTime.Zero, 0), new(motion.Duration, 1)]);
    }
}
