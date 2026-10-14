namespace AegiNext.Desktop.EffectLibrary.Effects;

/// <summary>携带本次脚本验证的原始异常，供宿主记录完整诊断。</summary>
public sealed class EffectScriptValidationFailedEventArgs(Exception error) : EventArgs
{
    public Exception Error { get; } = error;
}
