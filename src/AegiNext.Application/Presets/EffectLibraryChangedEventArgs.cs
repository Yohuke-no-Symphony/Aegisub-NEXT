namespace AegiNext.Application.Presets;

/// <summary>效果库完成一次提交或成功加载不同修订后的前后快照。</summary>
public sealed class EffectLibraryChangedEventArgs(EffectLibrarySnapshot previous, EffectLibrarySnapshot current) : EventArgs
{
    public EffectLibrarySnapshot Previous { get; } = previous;
    public EffectLibrarySnapshot Current { get; } = current;
}
