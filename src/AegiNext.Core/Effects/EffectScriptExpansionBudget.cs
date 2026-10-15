namespace AegiNext.Core.Effects;

internal sealed class EffectScriptExpansionBudget(CancellationToken cancellationToken)
{
    private int count;

    internal void Step(EffectScriptStatement statement)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (++count > 100000)
        {
            throw new EffectScriptException("单目标累计执行超过 100000 条声明的预算。", statement.Line, statement.Column);
        }
    }
}
