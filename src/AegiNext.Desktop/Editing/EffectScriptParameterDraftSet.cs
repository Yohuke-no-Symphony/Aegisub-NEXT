using System.Collections.Immutable;
using AegiNext.Core.Effects;

namespace AegiNext.Desktop.Editing;

/// <summary>组合模板参数草稿并原子捕获完整的强类型参数集合。</summary>
public sealed class EffectScriptParameterDraftSet
{
    private readonly EffectScript script;

    /// <summary>解析源码并应用持久参数或模板默认值。</summary>
    public EffectScriptParameterDraftSet(string source, IReadOnlyDictionary<string, EffectScriptParameterValue>? values = null)
    {
        script = EffectScriptParser.Parse(source);
        var bound = EffectScriptParameterBinder.Bind(script, values);
        Rows = script.Parameters.Select(parameter => new EffectScriptParameterDraft(parameter, bound[parameter.Name])).ToArray();
        foreach (var row in Rows)
        {
            row.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? Changed;
    public IReadOnlyList<EffectScriptParameterDraft> Rows { get; }
    public bool IsDirty => Rows.Any(row => row.IsDirty);
    public string? InvalidFieldKey => Rows.FirstOrDefault(row => row.Error is not null)?.InvalidFieldKey;

    /// <summary>全部字段有效时返回带默认值的不可变参数，失败时保留原文。</summary>
    public bool TryCapture(out ImmutableDictionary<string, EffectScriptParameterValue> values)
    {
        var builder = ImmutableDictionary.CreateBuilder<string, EffectScriptParameterValue>(StringComparer.Ordinal);
        var valid = true;
        foreach (var row in Rows)
        {
            if (row.TryGetValue(out var value))
            {
                builder[row.Name] = value;
            }
            else
            {
                valid = false;
            }
        }
        values = valid ? EffectScriptParameterBinder.Bind(script, builder) : ImmutableDictionary<string, EffectScriptParameterValue>.Empty;
        return valid;
    }

    /// <summary>接受成功提交的参数并清除其草稿脏状态。</summary>
    public void Accept(IReadOnlyDictionary<string, EffectScriptParameterValue> values)
    {
        foreach (var row in Rows)
        {
            row.Accept(values[row.Name]);
        }
    }

    /// <summary>将全部参数恢复为模板声明的默认值。</summary>
    public void ResetDefaults()
    {
        foreach (var row in Rows)
        {
            row.Choose(new(row.Label, row.Definition.DefaultValue));
        }
    }
}
