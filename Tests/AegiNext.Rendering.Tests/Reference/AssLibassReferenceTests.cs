using AegiNext.Core.Projects;
using AegiNext.SubtitleFormats;
using Xunit.Abstractions;

namespace AegiNext.Rendering.Tests.Reference;

/// <summary>固定字体下独立验证 libass 与导入后的本地渲染；容忍栅格化差异，不容忍计时语义变化。</summary>
public sealed class AssLibassReferenceTests(ITestOutputHelper output)
{
    [LibassReferenceFact]
    public void ConfiguredReferenceLoadsAndUsesThePinnedFont()
    {
        using var reference = new LibassReferenceRenderer();
        Assert.True(reference.Version >= 0x01702000);
        var frame = reference.Render(ReferenceSubtitleProject.Script("Wi"), 0);
        Assert.True(frame.Energy(3) > 100);
        Assert.True(frame.InkBounds().Right - frame.InkBounds().Left > 20);
        Assert.InRange(frame.ActiveRatio, 0.999, 1);
    }

    [LibassReferenceTheory]
    [InlineData("k")]
    [InlineData("ko")]
    public void CompleteMulticharacterGroupsActivateTogetherAtTheirOwnStart(string mode)
    {
        var source = ReferenceSubtitleProject.Script("{\\k50}{\\" + mode + "100}Wi", outline: "3");
        var document = ReferenceSubtitleProject.Import(source);
        var group = Assert.Single(document.Subtitles[0].Karaoke);
        Assert.Equal(2, group.Utf16Length);
        var exported = AssSubtitleFormat.Write(document).Text;
        using var native = ReferenceSubtitleProject.Renderer();
        using var reference = new LibassReferenceRenderer();
        foreach (var time in new long[] { 0, 499, 500, 1499, 1500 })
        {
            var expected = time < 500 ? 0 : 1;
            var external = reference.Render(source, time);
            var actual = ReferenceSubtitleProject.Render(native, document, time);
            Assert.InRange(Math.Abs(external.ActiveRatio - expected), 0, 0.001);
            Assert.InRange(Math.Abs(actual.ActiveRatio - expected), 0, 0.001);
            AssertRatio(external, reference.Render(exported, time), 0.001);
            if (mode == "ko" && time < 500)
            {
                Assert.Equal(0, external.Energy(2));
                Assert.Equal(0, actual.Energy(2));
            }
            else
            {
                Assert.True(external.Energy(2) > 1);
                Assert.True(actual.Energy(2) > 1);
            }
        }
    }

    [LibassReferenceTheory]
    [InlineData("Wiii")]
    [InlineData("ffi")]
    public void SweepUsesTheCompleteGroupAdvanceRatherThanRestartingAtEachCharacter(string text)
    {
        var source = ReferenceSubtitleProject.Script("{\\k50}{\\kf100}" + text);
        var document = ReferenceSubtitleProject.Import(source);
        Assert.Equal(text.Length, Assert.Single(document.Subtitles[0].Karaoke).Utf16Length);
        var exported = AssSubtitleFormat.Write(document).Text;
        using var native = ReferenceSubtitleProject.Renderer();
        using var reference = new LibassReferenceRenderer();
        var previous = 0d;
        foreach (var time in new long[] { 499, 700, 850, 1150, 1499, 1500 })
        {
            var external = reference.Render(source, time);
            var actual = ReferenceSubtitleProject.Render(native, document, time);
            AssertRatio(external, actual, 0.08);
            AssertRatio(external, reference.Render(exported, time), 0.005);
            Assert.True(external.ActiveRatio >= previous);
            previous = external.ActiveRatio;
            if (time == 850)
            {
                Assert.InRange(external.ActiveRatio, 0.05, 0.85);
                Assert.InRange(actual.ActiveRatio, 0.05, 0.85);
            }
        }
    }

    [LibassReferenceFact]
    public void AbsoluteKtOverlapAndReverseTextOrderKeepTheirIndependentActivations()
    {
        var source = ReferenceSubtitleProject.Script("{\\kt100\\k50}W{\\kt25\\k100}i{\\kt200\\k50}W");
        var document = ReferenceSubtitleProject.Import(source);
        Assert.Equal(3, document.Subtitles[0].Karaoke.Length);
        var exported = AssSubtitleFormat.Write(document);
        Assert.Contains("\\kt25", exported.Text, StringComparison.Ordinal);
        using var native = ReferenceSubtitleProject.Renderer();
        using var reference = new LibassReferenceRenderer();
        foreach (var time in new long[] { 0, 249, 250, 999, 1000, 1400, 1999, 2000, 2499 })
        {
            var external = reference.Render(source, time);
            AssertRatio(external, ReferenceSubtitleProject.Render(native, document, time), 0.035);
            AssertRatio(external, reference.Render(exported.Text, time), 0.005);
            if (time is 250 or 999)
            {
                Assert.InRange(external.ActiveRatio, 0.05, 0.4);
            }
        }
    }

    [LibassReferenceFact]
    public void CharacterizesReportedZeroOffsetShadowBackingTheStateSpecificAlpha()
    {
        var source = ReferenceSubtitleProject.Script("{\\k50}{\\k100\\1a&H80&\\2a&HC0&}Wi");
        var document = ReferenceSubtitleProject.Import(source);
        AssertShadowLoss(source, document);
        using var native = ReferenceSubtitleProject.Renderer();
        using var reference = new LibassReferenceRenderer();
        foreach (var time in new long[] { 499, 500, 1500 })
        {
            var external = reference.Render(source, time);
            var actual = ReferenceSubtitleProject.Render(native, document, time);
            var expected = time < 500 ? 63d / 255 : 127d / 255;
            Assert.InRange(Math.Abs(external.MaximumAlpha - expected), 0, 0.002);
            Assert.True(Math.Abs(actual.MaximumAlpha - expected) > 0.002);
            Assert.InRange(actual.MaximumAlpha, 0.999, 1);
            output.WriteLine($"zero shadow t={time}ms: libass alpha={external.MaximumAlpha:F6}; native alpha={actual.MaximumAlpha:F6}; equivalence tolerance=0.002");
        }
    }

    [LibassReferenceFact]
    public void StyleResetClearsBothKaraokeStateColorsAndAlphaWithoutResettingTheClock()
    {
        var source = ReferenceSubtitleProject.Script("{\\1c&H000000FF&\\2c&H0000FF00&\\alpha&H80&\\k100}Wi{\\r\\k100}Wi");
        var document = ReferenceSubtitleProject.Import(source);
        var exported = AssSubtitleFormat.Write(document).Text;
        using var native = ReferenceSubtitleProject.Renderer();
        using var reference = new LibassReferenceRenderer();
        foreach (var time in new long[] { 0, 999, 1000, 1500, 2000 })
        {
            var external = reference.Render(source, time);
            AssertRatio(external, ReferenceSubtitleProject.Render(native, document, time), 0.08);
            AssertRatio(external, reference.Render(exported, time), 0.005);
            Assert.InRange(Math.Abs(external.MaximumAlpha - 1), 0, 0.002);
        }
    }

    [LibassReferenceFact]
    public void EmptyRgbResetPreservesBothStateAlphasWithExplicitTransparentShadow()
    {
        var source = ReferenceSubtitleProject.Script("{\\k50}{\\k100\\1c&HFF0000&\\2c&HFF0000&\\1a&H80&\\2a&HC0&\\1c\\2c\\4a&HFF&}Wi");
        var document = ReferenceSubtitleProject.Import(source);
        var exported = AssSubtitleFormat.Write(document).Text;
        using var native = ReferenceSubtitleProject.Renderer();
        using var reference = new LibassReferenceRenderer();
        foreach (var time in new long[] { 0, 499, 500, 1499, 1500 })
        {
            var external = reference.Render(source, time);
            var actual = ReferenceSubtitleProject.Render(native, document, time);
            var exportedFrame = reference.Render(exported, time);
            var expectedAlpha = time < 500 ? 63d / 255 : 127d / 255;
            var expectedRatio = time < 500 ? 0 : 1;
            foreach (var frame in new[] { external, actual, exportedFrame })
            {
                Assert.InRange(Math.Abs(frame.MaximumAlpha - expectedAlpha), 0, 0.002);
                Assert.InRange(Math.Abs(frame.ActiveRatio - expectedRatio), 0, 0.001);
                Assert.Equal(0, frame.Energy(2));
            }
            AssertRatio(external, actual, 0.001);
            AssertRatio(external, exportedFrame, 0.001);
        }
    }

    [LibassReferenceTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void CharacterizesReportedOutlinedShadowExpansionAtEachBorderResolution(bool scaled)
    {
        var source = ReferenceSubtitleProject.Script("{\\bord6\\xshad8\\yshad4}Wi", scaled, 320, 180);
        var plain = ReferenceSubtitleProject.Script("Wi", scaled, 320, 180);
        var document = ReferenceSubtitleProject.Import(source);
        AssertShadowLoss(source, document);
        var baseDocument = ReferenceSubtitleProject.Import(plain);
        var exported = AssSubtitleFormat.Write(document).Text;
        using var native = ReferenceSubtitleProject.Renderer();
        using var reference = new LibassReferenceRenderer();
        var external = reference.Render(source, 0);
        var externalBase = reference.Render(plain, 0);
        var actual = ReferenceSubtitleProject.Render(native, document, 0);
        var actualBase = ReferenceSubtitleProject.Render(native, baseDocument, 0);
        var externalBounds = external.InkBounds();
        var externalBaseBounds = externalBase.InkBounds();
        var nativeBounds = actual.InkBounds();
        var nativeBaseBounds = actualBase.InkBounds();
        var externalRight = externalBounds.Right - externalBaseBounds.Right;
        var nativeRight = nativeBounds.Right - nativeBaseBounds.Right;
        var difference = externalRight - nativeRight;
        Assert.True(difference > 3);
        Assert.InRange(difference, scaled ? 12 : 6, scaled ? 14 : 8);
        output.WriteLine($"ScaledBorderAndShadow={scaled}: libass right expansion={externalRight}px; native={nativeRight}px; difference={difference}px exceeds equivalence tolerance=3px");
        Assert.InRange(Math.Abs((externalBaseBounds.Left - externalBounds.Left) -
            (nativeBaseBounds.Left - nativeBounds.Left)), 0, 3);
        Assert.InRange(Math.Abs(reference.Render(exported, 0).Energy(3) / external.Energy(3) - 1), 0, 0.02);
    }

    [LibassReferenceTheory]
    [InlineData("k")]
    [InlineData("ko")]
    public void CharacterizesReportedNativeShadowDifferenceAfterExternallyEquivalentNegativeInstantTransform(string mode)
    {
        using var reference = new LibassReferenceRenderer();
        var negative = ReferenceSubtitleProject.Script("{\\kt-50\\" + mode + "100\\t(-500,-500,\\bord10\\xshad12\\yshad8)}ab",
            outline: "2", shadow: "2");
        var negativeDirect = ReferenceSubtitleProject.Script("{\\kt-50\\" + mode + "100\\bord10\\xshad12\\yshad8}ab",
            outline: "2", shadow: "2");
        var zero = ReferenceSubtitleProject.Script("{\\kt0\\" + mode + "100\\t(0,0,\\bord10\\xshad12\\yshad8)}ab",
            outline: "2", shadow: "2");
        var direct = ReferenceSubtitleProject.Script("{\\kt0\\" + mode + "100\\bord10\\xshad12\\yshad8}ab",
            outline: "2", shadow: "2");
        foreach (var time in new long[] { 0, 250, 750 })
        {
            Assert.Equal(reference.Render(negativeDirect, time).Pixels, reference.Render(negative, time).Pixels);
            Assert.NotEqual(reference.Render(direct, time).Pixels, reference.Render(zero, time).Pixels);
        }
        var document = ReferenceSubtitleProject.Import(negative);
        AssertShadowLoss(negative, document);
        var exported = AssSubtitleFormat.Write(document).Text;
        using var native = ReferenceSubtitleProject.Renderer();
        var plain = ReferenceSubtitleProject.Script("{\\kt-50\\" + mode + "100}ab", outline: "2", shadow: "2");
        var plainDocument = ReferenceSubtitleProject.Import(plain);
        foreach (var time in new long[] { 0, 250, 750 })
        {
            var external = reference.Render(negative, time);
            Assert.InRange(Math.Abs(reference.Render(exported, time).Energy(3) / external.Energy(3) - 1), 0, 0.03);
            var externalBase = reference.Render(plain, time).InkBounds();
            var actual = ReferenceSubtitleProject.Render(native, document, time).InkBounds();
            var actualBase = ReferenceSubtitleProject.Render(native, plainDocument, time).InkBounds();
            var expected = external.InkBounds();
            var difference = (expected.Right - externalBase.Right) - (actual.Right - actualBase.Right);
            Assert.True(difference > 3);
            Assert.InRange(difference, 7, 9);
            output.WriteLine($"negative instant {mode}, t={time}ms: native shadow expansion deficit={difference}px; equivalence tolerance=3px");
        }
    }

    private static void AssertRatio(SubtitleReferenceFrame expected, SubtitleReferenceFrame actual, double tolerance)
    {
        Assert.True(expected.Energy(3) > 1 && actual.Energy(3) > 1);
        Assert.InRange(Math.Abs(expected.ActiveRatio - actual.ActiveRatio), 0, tolerance);
    }

    private static void AssertShadowLoss(string source, ProjectDocument document)
    {
        Assert.Contains(AssSubtitleFormat.Parse(source, 640, 360).Diagnostics, value => value.Code == "Ass.ShadowComposition");
        Assert.Contains(AssSubtitleFormat.Write(document).Diagnostics, value => value.Code == "Ass.ShadowComposition");
    }

    [LibassReferenceFact]
    public void ExternalOutlinedShadowRemainsExpandedWhenTheOutlineColorIsTransparent()
    {
        var source = ReferenceSubtitleProject.Script("{\\bord6\\xshad8\\yshad4\\3a&HFF&}Wi");
        var plain = ReferenceSubtitleProject.Script("{\\bord0\\xshad8\\yshad4\\3a&HFF&}Wi");
        using var reference = new LibassReferenceRenderer();
        var outlined = reference.Render(source, 0).InkBounds();
        var plainBounds = reference.Render(plain, 0).InkBounds();
        Assert.InRange(outlined.Right - plainBounds.Right, 5, 7);
        output.WriteLine($"Transparent outline still increases libass shadow right edge by {outlined.Right - plainBounds.Right}px.");
    }

    [LibassReferenceTheory]
    [InlineData("{\\1a&H80&\\4a&HFF&}Wi")]
    [InlineData("{\\bord6\\xshad8\\yshad4\\4a&HFF&}Wi")]
    public void TransparentShadowDoesNotGenerateTheCompositionLossDiagnostic(string text)
    {
        var source = ReferenceSubtitleProject.Script(text);
        var document = ReferenceSubtitleProject.Import(source);
        Assert.DoesNotContain(AssSubtitleFormat.Parse(source, 640, 360).Diagnostics, value => value.Code == "Ass.ShadowComposition");
        Assert.DoesNotContain(AssSubtitleFormat.Write(document).Diagnostics, value => value.Code == "Ass.ShadowComposition");
        using var reference = new LibassReferenceRenderer();
        using var native = ReferenceSubtitleProject.Renderer();
        var external = reference.Render(source, 0);
        var actual = ReferenceSubtitleProject.Render(native, document, 0);
        Assert.InRange(Math.Abs(external.MaximumAlpha - actual.MaximumAlpha), 0, 0.002);
    }
}
