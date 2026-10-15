using AegiNext.Core.Effects;
using AegiNext.Desktop.Editing;

namespace AegiNext.Desktop.Tests;

public sealed class EffectScriptParameterDraftTests
{
    private const string SOURCE = """
        effect "parameters" version 3
        var amount = 1 label "幅度" options ["Small": 1, "Large": 2]
        var duration = 0.1s suggest ["Slow": 0.5s]
        short-clip compress
        scope whole current
        segment hold flex 1
        at 0 opacity base
        at 1 opacity base
        end
        end
        """;

    [Fact]
    public void OptionsRejectCustomValuesAndSuggestionsAcceptThem()
    {
        var drafts = new EffectScriptParameterDraftSet(SOURCE);
        Assert.Equal("幅度", drafts.Rows[0].Label);
        drafts.Rows[0].RawText = "3";
        Assert.False(drafts.TryCapture(out _));
        Assert.Equal("3", drafts.Rows[0].RawText);
        drafts.Rows[0].Restore();
        drafts.Rows[1].RawText = "0.75s";
        Assert.True(drafts.TryCapture(out var values));
        Assert.Equal(new AegiNext.Core.Timing.MediaTime(3, 4), values["duration"].Duration);
    }

    [Fact]
    public void RestoreOnlyChangesTheRequestedFieldAndAcceptanceRetainsTypedValues()
    {
        var drafts = new EffectScriptParameterDraftSet(SOURCE);
        drafts.Rows[0].RawText = "2";
        drafts.Rows[1].RawText = "unfinished";
        drafts.Rows[1].Restore();
        Assert.True(drafts.IsDirty);
        Assert.Equal("2", drafts.Rows[0].RawText);
        Assert.True(drafts.TryCapture(out var values));
        drafts.Accept(values);
        Assert.False(drafts.IsDirty);
        Assert.Equal(2m, values["amount"].Number);
        Assert.Equal(EffectScriptParameterKind.DURATION, values["duration"].Kind);
    }

    [Fact]
    public void VectorBooleanStringAndLinearHdrColorRemainStronglyTyped()
    {
        var source = SOURCE.Replace("short-clip compress", """
            var active = true
            var title = "Author label"
            var direction = vec2(1, 2)
            var tilt = vec3(1, 2, 3)
            var color = rgba(2.5, 0.25, 0, 0.5)
            short-clip compress
            """, StringComparison.Ordinal);
        var drafts = new EffectScriptParameterDraftSet(source);
        drafts.Rows.Single(row => row.Name == "title").RawText = "Custom text";
        drafts.Rows.Single(row => row.Name == "active").RawText = "false";
        drafts.Rows.Single(row => row.Name == "tilt").Z.RawText = "720";
        Assert.True(drafts.TryCapture(out var values));
        Assert.False(values["active"].Boolean);
        Assert.Equal("Custom text", values["title"].Text);
        Assert.Equal(720, values["tilt"].Animation!.Value.Vector3.Z);
        Assert.Equal(2.5, values["color"].Animation!.Value.Color.Red);
        Assert.Equal(0.5, values["color"].Animation!.Value.Color.Alpha);
    }

    [Fact]
    public void EditingAnotherVectorCoordinatePreservesUntouchedDoublePrecision()
    {
        var source = SOURCE.Replace("short-clip compress", "var vector = vec3(1e-40, 1e40, 3)\nshort-clip compress", StringComparison.Ordinal);
        var drafts = new EffectScriptParameterDraftSet(source);
        var vector = drafts.Rows.Single(row => row.Name == "vector");
        vector.Z.RawText = "7";
        Assert.True(drafts.TryCapture(out var values));
        var value = values["vector"].Animation!.Value.Vector3;
        Assert.Equal(1e-40, value.X);
        Assert.Equal(1e40, value.Y);
        Assert.Equal(7, value.Z);
    }
}
