#include "prepared_overlay.h"
#include "media_core.h"
#include <algorithm>
#include <array>
#include <iostream>
#include <limits>
#include <stdexcept>
#include <string>
#include <vector>

using aeginext::encode::ColorPipeline;
using aeginext::encode::PreparedOverlay;
using aeginext::media::CoreError;
using aeginext::media::ErrorCode;

namespace
{
void Require(bool condition, const char *message)
{
    if (!condition)
    {
        throw std::runtime_error(message);
    }
}
an_export_overlay_info Info(uint32_t state, uint64_t revision)
{
    return {sizeof(an_export_overlay_info), 6, state, 0, revision};
}
void SetPixel(std::vector<float> &pixels, size_t width, size_t x, size_t y, const std::array<float, 4> &pixel)
{
    std::copy(pixel.begin(), pixel.end(), pixels.begin() + (y * width + x) * 4);
}
template<class Exception, class Action>
void Throws(Action action, const char *message)
{
    auto failed = false;
    try
    {
        action();
    }
    catch (const Exception &)
    {
        failed = true;
    }
    Require(failed, message);
}

void CoverageAndEmptyTransitions()
{
    const ColorPipeline color(AVCOL_SPC_BT709, AVCOL_PRI_BT709, AVCOL_TRC_BT709);
    std::vector<float> pixels(12 * 4 * 4);
    for (size_t pixel = 0; pixel < pixels.size(); pixel += 4)
    {
        pixels[pixel] = std::numeric_limits<float>::quiet_NaN();
        pixels[pixel + 1] = std::numeric_limits<float>::infinity();
        pixels[pixel + 2] = 4;
        pixels[pixel + 3] = (pixel % 8) ? -0.0f : 0.0f;
    }
    SetPixel(pixels, 12, 2, 1, {-0.125f, 0.5f, 2, 0.5f});
    SetPixel(pixels, 12, 8, 1, {-0.125f, 0.5f, 2, 0.5f});
    for (size_t x = 3; x < 7; ++x)
    {
        SetPixel(pixels, 12, x, 2, {1, 0, 0.5f, 1});
    }
    PreparedOverlay overlay(12, 4);
    overlay.Accept(Info(AN_EXPORT_OVERLAY_UPDATED, 9), pixels, color, 203, []() {});
    Require(overlay.IsValid() && overlay.Revision() == 9, "Updated revision was not committed");
    Require(overlay.ActivePixels() == 6 && overlay.PreparedPixels() == 6 && overlay.ForegroundPreparations() == 2,
        "Alpha coverage or bounded foreground counts are incorrect");
    Require(!overlay.Row(0).end && !overlay.Row(3).end, "Zero and negative-zero alpha were not skipped");
    Require(overlay.Row(1).begin == 2 && overlay.Row(1).end == 9 && overlay.Row(1).runCount == 2,
        "Sparse row coverage lost an active pixel or a transparent gap");
    Require(overlay.Row(2).begin == 3 && overlay.Row(2).end == 7 && overlay.Row(2).runCount == 1,
        "Continuous equal-color pixels were not merged");
    const auto storage = overlay.StorageBytes();
    overlay.Accept(Info(AN_EXPORT_OVERLAY_UNCHANGED, 9), {}, color, 203, []() {});
    Require(overlay.Revision() == 9 && overlay.ActivePixels() == 6 && overlay.StorageBytes() == storage,
        "Unchanged overlay rebuilt or discarded its foreground cache");
    overlay.Accept(Info(AN_EXPORT_OVERLAY_EMPTY, 10), {}, color, 203, []() {});
    Require(overlay.IsValid() && overlay.Revision() == 10 && !overlay.ActivePixels() && !overlay.PreparedPixels(),
        "Empty overlay kept previous active pixels");
    overlay.Accept(Info(AN_EXPORT_OVERLAY_UNCHANGED, 10), pixels, color, 203, []() {});
    Require(!overlay.ActivePixels(), "Unchanged after Empty consulted stale nonempty RGBA");
    overlay.Accept(Info(AN_EXPORT_OVERLAY_UPDATED, 11), pixels, color, 203, []() {});
    Require(overlay.ActivePixels() == 6, "Updated after Empty did not rebuild coverage");
}

void InvalidContractsInvalidateCachedState()
{
    const ColorPipeline color(AVCOL_SPC_BT709, AVCOL_PRI_BT709, AVCOL_TRC_BT709);
    std::vector<float> pixels(8 * 4 * 4);
    SetPixel(pixels, 8, 1, 1, {0.5f, 0.5f, 0.5f, 0.5f});
    PreparedOverlay overlay(8, 4);
    Throws<CoreError>([&]() { overlay.Accept(Info(AN_EXPORT_OVERLAY_UNCHANGED, 1), pixels, color, 203, []() {}); },
        "First-frame Unchanged was accepted");
    for (int variant = 0; variant < 7; ++variant)
    {
        overlay.Accept(Info(AN_EXPORT_OVERLAY_UPDATED, 2), pixels, color, 203, []() {});
        auto invalid = Info(AN_EXPORT_OVERLAY_UNCHANGED, 2);
        if (variant == 0) invalid.struct_size = 16;
        if (variant == 1) invalid.abi_version = 4;
        if (variant == 2) invalid.reserved = 1;
        if (variant == 3) invalid.revision = 0;
        if (variant == 4) invalid.state = 0;
        if (variant == 5) invalid.state = 4;
        if (variant == 6) invalid.revision = 3;
        auto rejected = false;
        try
        {
            overlay.Accept(invalid, pixels, color, 203, []() {});
        }
        catch (const CoreError &error)
        {
            rejected = error.Code() == ErrorCode::InvalidArgument;
        }
        Require(rejected && !overlay.IsValid() && !overlay.Revision() && !overlay.ActivePixels(),
            "Invalid callback contract retained a reusable revision");
    }
    Throws<CoreError>([&]()
    {
        overlay.Accept(Info(AN_EXPORT_OVERLAY_UPDATED, 4), std::span<const float>(pixels).first(pixels.size() - 1),
            color, 203, []() {});
    }, "Short updated buffer was accepted");
    overlay.Accept(Info(AN_EXPORT_OVERLAY_UPDATED, 5), pixels, color, 203, []() {});
    Throws<CoreError>([&]() { overlay.Accept(Info(AN_EXPORT_OVERLAY_UNCHANGED, 5), pixels, color, 406, []() {}); },
        "Changed reference white reused a prepared foreground");
    Require(!overlay.IsValid(), "Incompatible foreground retained a revision");
}

void ActiveInvalidValuesFailAndRecover()
{
    const ColorPipeline color(AVCOL_SPC_BT2020_NCL, AVCOL_PRI_BT2020, AVCOL_TRC_ARIB_STD_B67);
    PreparedOverlay overlay(8, 4);
    for (const auto alpha : {-0.25f, 1.25f, std::numeric_limits<float>::quiet_NaN(),
        std::numeric_limits<float>::infinity(), 0.5f})
    {
        overlay.Invalidate();
        std::vector<float> pixels(8 * 4 * 4);
        SetPixel(pixels, 8, 7, 3, {alpha == 0.5f ? std::numeric_limits<float>::quiet_NaN() : 1, 0, 0, alpha});
        Throws<std::invalid_argument>([&]()
        {
            overlay.Accept(Info(AN_EXPORT_OVERLAY_UPDATED, 1), pixels, color, 203, []() {});
        }, "Illegal active overlay sample was excluded from coverage");
        Require(!overlay.IsValid() && !overlay.Revision(), "Failed foreground preparation published its revision");
        SetPixel(pixels, 8, 7, 3, {-0.125f, 0.75f, 4, 0.5f});
        overlay.Accept(Info(AN_EXPORT_OVERLAY_UPDATED, 2), pixels, color, 203, []() {});
        Require(overlay.ActivePixels() == 1 && overlay.PreparedPixels() == 1, "Failure did not recover on a fresh update");
    }
}

void CancelledCoverageNeverPublishesAndUnchangedChecksCancellation()
{
    const ColorPipeline color(AVCOL_SPC_BT709, AVCOL_PRI_BT709, AVCOL_TRC_BT709);
    std::vector<float> pixels(8 * 68 * 4, 0.5f);
    PreparedOverlay overlay(8, 68);
    int checks = 0;
    Throws<CoreError>([&]()
    {
        overlay.Accept(Info(AN_EXPORT_OVERLAY_UPDATED, 1), pixels, color, 203, [&]()
        {
            if (++checks == 3) throw CoreError(ErrorCode::Cancelled, "controlled coverage cancellation");
        });
    }, "Coverage did not check cancellation during its rows");
    Require(!overlay.IsValid() && !overlay.Revision(), "Cancelled coverage published a partial cache");
    overlay.Accept(Info(AN_EXPORT_OVERLAY_UPDATED, 2), pixels, color, 203, []() {});
    Throws<CoreError>([&]()
    {
        overlay.Accept(Info(AN_EXPORT_OVERLAY_UNCHANGED, 2), {}, color, 203,
            []() { throw CoreError(ErrorCode::Cancelled, "controlled unchanged cancellation"); });
    }, "Unchanged overlay ignored cancellation");
    Require(!overlay.IsValid(), "Cancelled unchanged callback retained a reusable cache");
}

void ColorAndRunBudgetsFallbackWithoutFrameSizedStorage()
{
    const ColorPipeline color(AVCOL_SPC_BT709, AVCOL_PRI_BT709, AVCOL_TRC_BT709);
    constexpr size_t WIDE = 1152;
    std::vector<float> distinct(WIDE * 2 * 4);
    for (size_t y = 0; y < 2; ++y)
    {
        for (size_t x = 0; x < WIDE; ++x)
        {
            SetPixel(distinct, WIDE, x, y, {static_cast<float>(y * WIDE + x) / 4096, 0.25f, 0.5f, 0.5f});
        }
    }
    PreparedOverlay colors(WIDE, 2);
    colors.Accept(Info(AN_EXPORT_OVERLAY_UPDATED, 1), distinct, color, 203, []() {});
    Require(colors.ActivePixels() == WIDE * 2 && !colors.PreparedPixels() && colors.ForegroundPreparations() == 1024,
        "Unique-color budget did not fall back whole rows");
    Require(colors.StorageBytes() < 512 * 1024, "Unique colors allocated a full-frame prepared array");

    constexpr size_t TALL = 8200;
    std::vector<float> alternating(4 * TALL * 4);
    for (size_t y = 0; y < TALL; ++y)
    {
        for (size_t x = 0; x < 4; ++x)
        {
            SetPixel(alternating, 4, x, y, {x % 2 ? 0.5f : 0.25f, 0.25f, 0.5f, 0.5f});
        }
    }
    PreparedOverlay runs(4, TALL);
    runs.Accept(Info(AN_EXPORT_OVERLAY_UPDATED, 1), alternating, color, 203, []() {});
    Require(runs.ActivePixels() == 4 * TALL && runs.PreparedPixels() == 16384 && !runs.Row(4096).runCount,
        "Run budget did not retain bounded cached rows and fall back later rows");
    Require(runs.StorageBytes() < 512 * 1024, "Run cache expanded beyond its fixed budget");
    const auto bytes = runs.StorageBytes();
    runs.Accept(Info(AN_EXPORT_OVERLAY_UPDATED, 2), alternating, color, 203, []() {});
    Require(runs.StorageBytes() == bytes, "Repeated updates grew retained cache storage");
    PreparedOverlay fourK(3840, 2160);
    fourK.Accept(Info(AN_EXPORT_OVERLAY_EMPTY, 1), {}, color, 203, []() {});
    Require(fourK.StorageBytes() < 64 * 1024, "Empty 4K cache allocated pixel-sized prepared storage");
}

void ConsecutiveUpdatesDelayCoverageUntilStable()
{
    const ColorPipeline color(AVCOL_SPC_BT709, AVCOL_PRI_BT709, AVCOL_TRC_BT709);
    std::vector<float> pixels(8 * 4 * 4, 0.5f);
    PreparedOverlay overlay(8, 4);
    overlay.Accept(Info(AN_EXPORT_OVERLAY_UPDATED, 1), pixels, color, 203, []() {});
    Require(overlay.CoverageKnown() && overlay.ScannedPixels() == 32, "First update did not build coverage");
    pixels[0] = 0.25f;
    overlay.Accept(Info(AN_EXPORT_OVERLAY_UPDATED, 2), pixels, color, 203, []() {});
    Require(!overlay.CoverageKnown() && !overlay.ScannedPixels() && !overlay.ForegroundPreparations() &&
        overlay.Row(0).begin == 0 && overlay.Row(0).end == 8, "Consecutive update performed a redundant full scan");
    pixels[0] = 0.125f;
    overlay.Accept(Info(AN_EXPORT_OVERLAY_UPDATED, 3), pixels, color, 203, []() {});
    Require(!overlay.CoverageKnown(), "Continuous animation accidentally rebuilt coverage");
    overlay.Accept(Info(AN_EXPORT_OVERLAY_UNCHANGED, 3), {}, color, 203, []() {});
    Require(overlay.CoverageKnown() && overlay.ScannedPixels() == 32 && overlay.ActivePixels() == 32,
        "First stable frame did not build retained coverage");
    overlay.Accept(Info(AN_EXPORT_OVERLAY_UNCHANGED, 3), {}, color, 203, []() {});
    Require(!overlay.ScannedPixels(), "Stable unchanged frame rescanned its overlay");
    overlay.Accept(Info(AN_EXPORT_OVERLAY_UPDATED, 4), pixels, color, 203, []() {});
    overlay.Accept(Info(AN_EXPORT_OVERLAY_UPDATED, 5), pixels, color, 203, []() {});
    pixels[3] = -0.125f;
    Throws<std::invalid_argument>([&]()
    {
        overlay.Accept(Info(AN_EXPORT_OVERLAY_UNCHANGED, 5), {}, color, 203, []() {});
    }, "Deferred coverage ignored an illegal active alpha");
    Require(!overlay.IsValid(), "Failed deferred build published a reusable cache");
    pixels[3] = 0.5f;
    overlay.Accept(Info(AN_EXPORT_OVERLAY_UPDATED, 6), pixels, color, 203, []() {});
    overlay.Accept(Info(AN_EXPORT_OVERLAY_UPDATED, 7), pixels, color, 203, []() {});
    Throws<CoreError>([&]()
    {
        overlay.Accept(Info(AN_EXPORT_OVERLAY_UNCHANGED, 7), {}, color, 203,
            []() { throw CoreError(ErrorCode::Cancelled, "cancel delayed build"); });
    }, "Deferred coverage ignored cancellation");
    Require(!overlay.IsValid(), "Cancelled deferred coverage retained an adaptive cache");
}
}

int main()
{
    try
    {
        CoverageAndEmptyTransitions();
        InvalidContractsInvalidateCachedState();
        ActiveInvalidValuesFailAndRecover();
        CancelledCoverageNeverPublishesAndUnchangedChecksCancellation();
        ColorAndRunBudgetsFallbackWithoutFrameSizedStorage();
        ConsecutiveUpdatesDelayCoverageUntilStable();
        std::cout << "PASS ABI5 overlay transitions, active/-0 coverage, finite validation, transactional cancellation, bounded color/run caches\n";
        return 0;
    }
    catch (const std::exception &error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}
