#include "prepared_overlay.h"
#include "media_core.h"
#include <algorithm>
#include <bit>
#include <cmath>
#include <limits>
#include <stdexcept>

namespace aeginext::encode
{
namespace
{
constexpr size_t MAX_COLORS = 1024;
constexpr size_t COLOR_SLOTS = 2048;
constexpr size_t MAX_RUNS = 16384;
constexpr size_t NO_COLOR = std::numeric_limits<size_t>::max();

void InvalidContract(const char *message)
{
    throw aeginext::media::CoreError(aeginext::media::ErrorCode::InvalidArgument, message);
}
}

PreparedOverlay::PreparedOverlay(uint32_t width, uint32_t height)
    : width_(width), height_(height)
{
    if (!width || !height || static_cast<uint64_t>(width) * height > 33177600)
    {
        throw std::invalid_argument("Invalid overlay dimensions");
    }
    rows_.resize(height);
}

void PreparedOverlay::Accept(const an_export_overlay_info &info, std::span<const float> pixels,
    const ColorPipeline &color, double referenceWhite, const std::function<void()> &checkCancel)
{
    try
    {
        scannedPixels_ = 0;
        if (info.struct_size != sizeof(info) || info.abi_version != AN_EXPORT_ABI_VERSION || info.reserved || !info.revision)
        {
            InvalidContract("Invalid overlay information ABI or revision");
        }
        checkCancel();
        if (!std::isfinite(referenceWhite) || referenceWhite <= 0)
        {
            throw std::invalid_argument("Invalid subtitle opacity or reference white");
        }
        if (info.state == AN_EXPORT_OVERLAY_UNCHANGED)
        {
            if (!valid_ || info.revision != revision_ || !IsCompatible(color, referenceWhite))
            {
                InvalidContract("Unchanged overlay requires the last successful revision");
            }
            if (!coverageKnown_)
            {
                Build(pixels_, color, referenceWhite, checkCancel);
                coverageKnown_ = true;
            }
            checkCancel();
            lastState_ = info.state;
            return;
        }

        const auto consecutiveUpdate = valid_ && lastState_ == AN_EXPORT_OVERLAY_UPDATED;
        Invalidate();
        if (info.state == AN_EXPORT_OVERLAY_UPDATED)
        {
            if (pixels.size() < static_cast<size_t>(width_) * height_ * 4)
            {
                InvalidContract("Updated overlay requires a complete RGBA buffer");
            }
            if (consecutiveUpdate)
            {
                std::fill(rows_.begin(), rows_.end(), OverlayRow{0, width_, 0, 0});
            }
            else
            {
                Build(pixels, color, referenceWhite, checkCancel);
                coverageKnown_ = true;
            }
            pixels_ = pixels;
        }
        else if (info.state == AN_EXPORT_OVERLAY_EMPTY)
        {
            std::fill(rows_.begin(), rows_.end(), OverlayRow{});
            coverageKnown_ = true;
        }
        else
        {
            InvalidContract("Unknown overlay state");
        }
        checkCancel();
        color_ = &color;
        referenceWhite_ = referenceWhite;
        revision_ = info.revision;
        lastState_ = info.state;
        valid_ = true;
    }
    catch (...)
    {
        Invalidate();
        throw;
    }
}

void PreparedOverlay::Build(std::span<const float> pixels, const ColorPipeline &color, double referenceWhite,
    const std::function<void()> &checkCancel)
{
    if (!std::isfinite(referenceWhite) || referenceWhite <= 0)
    {
        throw std::invalid_argument("Invalid subtitle opacity or reference white");
    }
    colors_.resize(COLOR_SLOTS);
    std::fill(colors_.begin(), colors_.end(), OverlayColorSlot{});
    runs_.reserve(MAX_RUNS);
    scannedPixels_ = static_cast<uint64_t>(width_) * height_;
    for (uint32_t y = 0; y < height_; ++y)
    {
        if (!(y % 32))
        {
            checkCancel();
        }
        auto &row = rows_[y];
        row = {width_, 0, runs_.size(), 0};
        auto prepared = true;
        for (uint32_t x = 0; x < width_; ++x)
        {
            const auto *sample = &pixels[(static_cast<size_t>(y) * width_ + x) * 4];
            if (sample[3] == 0)
            {
                continue;
            }
            const std::array<float, 4> pixel{sample[0], sample[1], sample[2], sample[3]};
            for (const auto value : pixel)
            {
                if (!std::isfinite(value))
                {
                    throw std::invalid_argument("Non-finite subtitle sample");
                }
            }
            if (sample[3] < 0 || sample[3] > 1)
            {
                throw std::invalid_argument("Invalid subtitle opacity or reference white");
            }
            row.begin = std::min(row.begin, x);
            row.end = x + 1;
            ++activePixels_;
            if (!prepared)
            {
                continue;
            }
            const auto slot = FindColor(pixel, color, referenceWhite);
            if (slot != NO_COLOR && row.runCount && runs_.back().end == x && runs_.back().color == slot)
            {
                ++runs_.back().end;
            }
            else if (slot != NO_COLOR && runs_.size() < MAX_RUNS)
            {
                runs_.push_back({x, x + 1, static_cast<uint32_t>(slot)});
                ++row.runCount;
            }
            else
            {
                runs_.resize(row.firstRun);
                row.runCount = 0;
                prepared = false;
            }
        }
        for (const auto &run : Runs(row))
        {
            preparedPixels_ += run.end - run.begin;
        }
    }
}

size_t PreparedOverlay::FindColor(const std::array<float, 4> &pixel, const ColorPipeline &color, double referenceWhite)
{
    std::array<uint32_t, 4> key{};
    uint64_t hash = 14695981039346656037ULL;
    for (size_t channel = 0; channel < pixel.size(); ++channel)
    {
        key[channel] = std::bit_cast<uint32_t>(pixel[channel]);
        hash = (hash ^ key[channel]) * 1099511628211ULL;
    }
    auto slot = static_cast<size_t>(hash % COLOR_SLOTS);
    while (colors_[slot].occupied)
    {
        if (colors_[slot].key == key)
        {
            return slot;
        }
        slot = (slot + 1) % COLOR_SLOTS;
    }
    if (foregroundPreparations_ >= MAX_COLORS)
    {
        return NO_COLOR;
    }
    colors_[slot] = {key, color.PrepareForeground(pixel, referenceWhite), true};
    ++foregroundPreparations_;
    return slot;
}

void PreparedOverlay::Invalidate()
{
    valid_ = false;
    revision_ = 0;
    activePixels_ = 0;
    preparedPixels_ = 0;
    foregroundPreparations_ = 0;
    scannedPixels_ = 0;
    coverageKnown_ = false;
    lastState_ = 0;
    color_ = nullptr;
    referenceWhite_ = 0;
    pixels_ = {};
    runs_.clear();
}
bool PreparedOverlay::IsValid() const { return valid_; }
bool PreparedOverlay::IsCompatible(const ColorPipeline &color, double referenceWhite) const
{
    return valid_ && color_ == &color && referenceWhite_ == referenceWhite;
}
uint32_t PreparedOverlay::Width() const { return width_; }
uint32_t PreparedOverlay::Height() const { return height_; }
uint64_t PreparedOverlay::Revision() const { return revision_; }
uint64_t PreparedOverlay::ActivePixels() const { return activePixels_; }
uint64_t PreparedOverlay::PreparedPixels() const { return preparedPixels_; }
uint64_t PreparedOverlay::ForegroundPreparations() const { return foregroundPreparations_; }
uint64_t PreparedOverlay::ScannedPixels() const { return scannedPixels_; }
bool PreparedOverlay::CoverageKnown() const { return coverageKnown_; }
std::span<const float> PreparedOverlay::Pixels() const { return pixels_; }
const OverlayRow &PreparedOverlay::Row(size_t row) const { return rows_[row]; }
std::span<const OverlayRun> PreparedOverlay::Runs(const OverlayRow &row) const
{
    return std::span<const OverlayRun>(runs_).subspan(row.firstRun, row.runCount);
}
const PreparedForeground &PreparedOverlay::Foreground(uint32_t color) const { return colors_[color].foreground; }
size_t PreparedOverlay::StorageBytes() const
{
    return rows_.capacity() * sizeof(OverlayRow) + runs_.capacity() * sizeof(OverlayRun) +
        colors_.capacity() * sizeof(OverlayColorSlot);
}
}
