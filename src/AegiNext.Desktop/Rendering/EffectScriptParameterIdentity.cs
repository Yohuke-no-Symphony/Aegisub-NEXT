using System.Collections.Immutable;
using System.Text.Json;
using System.Globalization;
using AegiNext.Core.Effects;

namespace AegiNext.Desktop.Rendering;

internal static class EffectScriptParameterIdentity
{
    internal static ImmutableDictionary<string, EffectScriptParameterValue> Normalize(string source,
        ImmutableDictionary<string, EffectScriptParameterValue>? values = null)
    {
        try
        {
            return EffectScriptParameterBinder.Bind(EffectScriptParser.Parse(source), values);
        }
        catch (Exception error) when (error is FormatException or ArgumentException or InvalidOperationException)
        {
            return values ?? ImmutableDictionary<string, EffectScriptParameterValue>.Empty;
        }
    }

    internal static string Create(ImmutableDictionary<string, EffectScriptParameterValue> values) =>
        JsonSerializer.Serialize(values.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair =>
            new KeyValuePair<string, string>(pair.Key, ((int)pair.Value.Kind).ToString(CultureInfo.InvariantCulture) + ":" + Value(pair.Value))));

    private static string Value(EffectScriptParameterValue value) => value.Kind switch
    {
        EffectScriptParameterKind.NUMBER => value.Number.ToString("G29", CultureInfo.InvariantCulture),
        EffectScriptParameterKind.DURATION => string.Create(CultureInfo.InvariantCulture, $"{value.Duration.Numerator}/{value.Duration.Denominator}"),
        EffectScriptParameterKind.BOOLEAN => value.Boolean ? "true" : "false",
        EffectScriptParameterKind.STRING => JsonSerializer.Serialize(value.Text),
        _ => value.Animation is { } animation ? string.Join(',', Enumerable.Range(0, animation.ComponentCount)
            .Select(index => animation.GetComponent(index) is var component && component != 0
                ? component.ToString("R", CultureInfo.InvariantCulture) : "0")) : "null"
    };
}
