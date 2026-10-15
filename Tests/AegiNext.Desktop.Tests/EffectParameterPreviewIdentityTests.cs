using System.Collections.Immutable;
using AegiNext.Core.Effects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Rendering;

namespace AegiNext.Desktop.Tests;

public sealed class EffectParameterPreviewIdentityTests
{
    [Fact]
    public void EqualDecimalValuesAndDictionaryOrderHaveOneCanonicalIdentity()
    {
        var first = ImmutableDictionary<string, EffectScriptParameterValue>.Empty
            .Add("amount", EffectScriptParameterValue.FromNumber(0.50m))
            .Add("time", EffectScriptParameterValue.FromDuration(new(2, 6)));
        var second = ImmutableDictionary<string, EffectScriptParameterValue>.Empty
            .Add("time", EffectScriptParameterValue.FromDuration(new(1, 3)))
            .Add("amount", EffectScriptParameterValue.FromNumber(0.5m));
        Assert.Equal(EffectScriptParameterIdentity.Create(first), EffectScriptParameterIdentity.Create(second));
    }
    [Fact]
    public void DefaultAndExplicitParameterSetsShareIdentityWhileChangedValuesInvalidateBothPreviews()
    {
        var template = BuiltinEffectScripts.Get("fade-in");
        var defaults = EffectScriptParameterBinder.Bind(template.Script);
        var implicitRequest = new EffectPresetPreviewRequest("fade", template.Source, 1, 1, null);
        var explicitRequest = new EffectPresetPreviewRequest("fade", template.Source, 2, 1, null, defaults);
        Assert.Equal(implicitRequest.SourceHash, explicitRequest.SourceHash);
        var changed = defaults.SetItem("duration", EffectScriptParameterValue.FromDuration(new(1, 2)));
        var changedRequest = new EffectPresetPreviewRequest("fade", template.Source, 3, 1, null, changed);
        Assert.NotEqual(explicitRequest.SourceHash, changedRequest.SourceHash);
        var originalInput = new EffectScriptPreviewInput(template.Source, new());
        var sameInput = originalInput with { Parameters = defaults };
        var changedInput = originalInput with { Parameters = changed };
        Assert.True(originalInput.HasSameScene(sameInput));
        Assert.False(originalInput.HasSameScene(changedInput));
    }

    [Fact]
    public void ParameterChangesReplaceTheCompiledSceneAndChangeRenderedFrames()
    {
        var template = BuiltinEffectScripts.Get("fade-in");
        var input = new EffectScriptPreviewInput(template.Source, new());
        using var renderer = new EffectScriptPreviewRenderer();
        var first = renderer.Render(new(Guid.NewGuid(), 1, input), new MediaTime(1, 10), CancellationToken.None);
        var changed = input with
        {
            Parameters = ImmutableDictionary<string, EffectScriptParameterValue>.Empty
                .Add("duration", EffectScriptParameterValue.FromDuration(new(1, 2)))
        };
        var second = renderer.Render(new(Guid.NewGuid(), 2, changed), new MediaTime(1, 10), CancellationToken.None);
        Assert.NotEqual(first.Pixels.ToArray(), second.Pixels.ToArray());
        Assert.Equal(2, renderer.CompilationCount);
        Assert.Equal(1, renderer.CachedSceneCount);
    }
}
