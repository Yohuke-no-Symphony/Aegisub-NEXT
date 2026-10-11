#include "yuv_frame_pipeline.h"
#include "frame_row_executor.h"
#include "legacy_yuv_frame_pipeline.h"
#include "prepared_overlay.h"
#include <algorithm>
#include <array>
#include <atomic>
#include <chrono>
#include <condition_variable>
#include <cstdint>
#include <cstring>
#include <iostream>
#include <limits>
#include <mutex>
#include <set>
#include <stdexcept>
#include <string>
#include <thread>
#include <vector>

extern "C"
{
#include <libavutil/pixdesc.h>
}

using aeginext::encode::ColorPipeline;
using aeginext::encode::FrameRowExecutor;
using aeginext::encode::YuvFramePipeline;
using aeginext::encode::PreparedOverlay;
using aeginext::encode::tests::LegacyYuvFramePipeline;
using aeginext::media::CoreError;
using aeginext::media::ErrorCode;
using aeginext::media::FramePointer;

namespace
{
constexpr int WIDTH = 32;
constexpr int HEIGHT = 36;
constexpr uint8_t PADDING_SENTINEL = 0xa5;

void Require(bool value, const std::string &message)
{
    if (!value)
    {
        throw std::runtime_error(message);
    }
}

void Check(int result, const char *operation)
{
    if (result < 0)
    {
        char text[AV_ERROR_MAX_STRING_SIZE]{};
        av_strerror(result, text, sizeof(text));
        throw std::runtime_error(std::string(operation) + ": " + text);
    }
}

int ComponentWidth(const AVFrame *frame, int component)
{
    const auto *descriptor = av_pix_fmt_desc_get(static_cast<AVPixelFormat>(frame->format));
    const auto shift = component == 0 ? 0 : descriptor->log2_chroma_w;
    return (frame->width + (1 << shift) - 1) >> shift;
}

int ComponentHeight(const AVFrame *frame, int component)
{
    const auto *descriptor = av_pix_fmt_desc_get(static_cast<AVPixelFormat>(frame->format));
    const auto shift = component == 0 ? 0 : descriptor->log2_chroma_h;
    return (frame->height + (1 << shift) - 1) >> shift;
}

FramePointer Allocate(AVPixelFormat format, int width, int height)
{
    FramePointer frame(av_frame_alloc());
    Require(frame != nullptr, "Cannot allocate pipeline fixture frame");
    frame->format = format;
    frame->width = width;
    frame->height = height;
    Check(av_frame_get_buffer(frame.get(), 64), "pipeline fixture buffer");
    for (const auto *buffer : frame->buf)
    {
        if (buffer)
        {
            std::memset(buffer->data, PADDING_SENTINEL, buffer->size);
        }
    }
    return frame;
}

void FillSource(AVFrame *frame, int seed)
{
    const auto *descriptor = av_pix_fmt_desc_get(static_cast<AVPixelFormat>(frame->format));
    for (int component = 0; component < 3; ++component)
    {
        const auto &storage = descriptor->comp[component];
        const auto maximum = (uint32_t{1} << storage.depth) - 1;
        const auto limited = frame->color_range == AVCOL_RANGE_MPEG;
        const auto yOffset = limited ? maximum * 16 / 255 : 0;
        const auto yScale = limited ? maximum * 219 / 255 : maximum;
        for (int y = 0; y < ComponentHeight(frame, component); ++y)
        {
            for (int x = 0; x < ComponentWidth(frame, component); ++x)
            {
                const auto variation = (x * 17 + y * 11 + component * 19 + seed * 7) % 81;
                const auto code = component == 0 ? yOffset + yScale * (140 + variation) / 255 :
                    maximum * (116 + variation % 25) / 255;
                auto *sample = frame->data[storage.plane] + y * frame->linesize[storage.plane] +
                    x * storage.step + storage.offset;
                if (storage.depth + storage.shift <= 8)
                {
                    *sample = static_cast<uint8_t>(code << storage.shift);
                }
                else
                {
                    const auto word = static_cast<uint16_t>(code << storage.shift);
                    std::memcpy(sample, &word, sizeof(word));
                }
            }
        }
    }
}

FramePointer Source(AVPixelFormat format, AVColorRange range,
    AVColorTransferCharacteristic transfer, int layout, int seed = 0, int width = WIDTH, int height = HEIGHT)
{
    const auto cropped = layout != 0;
    auto frame = Allocate(format, width + (cropped ? 3 : 0), height + (cropped ? 3 : 0));
    const auto hdr = transfer == AVCOL_TRC_SMPTE2084 || transfer == AVCOL_TRC_ARIB_STD_B67;
    frame->color_range = range;
    frame->colorspace = hdr ? AVCOL_SPC_BT2020_NCL : AVCOL_SPC_BT709;
    frame->color_primaries = hdr ? AVCOL_PRI_BT2020 : AVCOL_PRI_BT709;
    frame->color_trc = transfer;
    frame->chroma_location = AVCHROMA_LOC_LEFT;
    frame->sample_aspect_ratio = {1, 1};
    frame->pts = 3100 + seed * 47;
    frame->duration = seed % 2 ? 47 : 33;
    frame->time_base = {1, 1000};
    if (cropped)
    {
        frame->crop_left = 1;
        frame->crop_top = 1;
        frame->crop_right = 2;
        frame->crop_bottom = 2;
    }
    FillSource(frame.get(), seed);
    if (layout == 2)
    {
        const auto *descriptor = av_pix_fmt_desc_get(format);
        std::array<bool, AV_NUM_DATA_POINTERS> visited{};
        for (int component = 0; component < 3; ++component)
        {
            const auto plane = descriptor->comp[component].plane;
            if (visited[plane])
            {
                continue;
            }
            visited[plane] = true;
            frame->data[plane] += (ComponentHeight(frame.get(), component) - 1) * frame->linesize[plane];
            frame->linesize[plane] = -frame->linesize[plane];
        }
    }
    return frame;
}

std::vector<std::vector<uint8_t>> SnapshotBuffers(const AVFrame *frame)
{
    std::vector<std::vector<uint8_t>> result;
    for (const auto *buffer : frame->buf)
    {
        result.emplace_back(buffer ? buffer->size : 0);
        if (buffer)
        {
            std::memcpy(result.back().data(), buffer->data, buffer->size);
        }
    }
    return result;
}

std::vector<int64_t> SnapshotFacts(const AVFrame *frame)
{
    std::vector<int64_t> result
    {
        frame->format, frame->width, frame->height,
        static_cast<int64_t>(frame->crop_left), static_cast<int64_t>(frame->crop_top),
        static_cast<int64_t>(frame->crop_right), static_cast<int64_t>(frame->crop_bottom),
        frame->pts, frame->duration, frame->time_base.num, frame->time_base.den,
        frame->color_range, frame->colorspace, frame->color_primaries, frame->color_trc,
        frame->chroma_location, frame->sample_aspect_ratio.num, frame->sample_aspect_ratio.den
    };
    for (int plane = 0; plane < AV_NUM_DATA_POINTERS; ++plane)
    {
        result.push_back(frame->linesize[plane]);
        result.push_back(static_cast<int64_t>(reinterpret_cast<uintptr_t>(frame->data[plane])));
    }
    return result;
}

std::vector<float> Layer(int width, int height, int mode)
{
    std::vector<float> result(static_cast<size_t>(width) * height * 4);
    for (int y = 0; y < height; ++y)
    {
        for (int x = 0; x < width; ++x)
        {
            if (mode == 0 || (mode == 3 && (x * 13 + y * 7) % 11 != 0))
            {
                continue;
            }
            const auto alpha = mode == 1 ? 1.0f : mode == 4 ? 0.25f : 0.5f;
            const auto offset = (static_cast<size_t>(y) * width + x) * 4;
            result[offset] = (mode == 4 ? -0.125f : 4.0f) * alpha;
            result[offset + 1] = 2.0f * alpha;
            result[offset + 2] = 0.5f * alpha;
            result[offset + 3] = alpha;
        }
    }
    return result;
}

void EqualSamples(const AVFrame *expected, const AVFrame *actual, const std::string &context)
{
    Require(expected->format == actual->format && expected->width == actual->width && expected->height == actual->height,
        context + ": encoder input layout changed");
    const auto *descriptor = av_pix_fmt_desc_get(static_cast<AVPixelFormat>(expected->format));
    for (int component = 0; component < 3; ++component)
    {
        const auto &storage = descriptor->comp[component];
        const auto bytes = storage.depth + storage.shift <= 8 ? 1 : 2;
        for (int y = 0; y < ComponentHeight(expected, component); ++y)
        {
            for (int x = 0; x < ComponentWidth(expected, component); ++x)
            {
                const auto *before = expected->data[storage.plane] + y * expected->linesize[storage.plane] +
                    x * storage.step + storage.offset;
                const auto *after = actual->data[storage.plane] + y * actual->linesize[storage.plane] +
                    x * storage.step + storage.offset;
                if (std::memcmp(before, after, bytes) != 0)
                {
                    uint16_t first = 0;
                    uint16_t second = 0;
                    std::memcpy(&first, before, bytes);
                    std::memcpy(&second, after, bytes);
                    throw std::runtime_error(context + ": component=" + std::to_string(component) +
                        " sample=(" + std::to_string(x) + "," + std::to_string(y) + ") legacy=" +
                        std::to_string(first) + " optimized=" + std::to_string(second));
                }
            }
        }
    }
}

void Convert(YuvFramePipeline &pipeline, const AVFrame *source, std::span<const float> layer,
    const ColorPipeline &color, double referenceWhite, AVFrame *output)
{
    Check(pipeline.Upsample(source), "optimized upsample");
    pipeline.Composite(source, layer, color, referenceWhite, []() {});
    Check(pipeline.Downsample(output), "optimized downsample");
}

size_t LegacyMatrixParity()
{
    size_t comparisons = 0;
    for (const auto format : {AV_PIX_FMT_NV12, AV_PIX_FMT_P010LE, AV_PIX_FMT_YUV420P,
        AV_PIX_FMT_YUV420P10LE, AV_PIX_FMT_YUV444P16LE})
    {
        for (const auto range : {AVCOL_RANGE_MPEG, AVCOL_RANGE_JPEG})
        {
            for (const auto transfer : {AVCOL_TRC_BT709, AVCOL_TRC_IEC61966_2_1,
                AVCOL_TRC_SMPTE2084, AVCOL_TRC_ARIB_STD_B67})
            {
                for (int layout = 0; layout < 3; ++layout)
                {
                    auto source = Source(format, range, transfer, layout);
                    const auto originalBuffers = SnapshotBuffers(source.get());
                    const auto originalFacts = SnapshotFacts(source.get());
                    const ColorPipeline color(source->colorspace, source->color_primaries, source->color_trc);
                    for (const auto outputFormat : {AV_PIX_FMT_YUV420P, AV_PIX_FMT_NV12,
                        AV_PIX_FMT_YUV420P10LE, AV_PIX_FMT_P010LE})
                    {
                        LegacyYuvFramePipeline reference(source.get(), WIDTH, HEIGHT, outputFormat);
                        auto expected = Allocate(outputFormat, WIDTH, HEIGHT);
                        for (const auto threads : {1, 2, 4})
                        {
                            YuvFramePipeline pipeline(source.get(), WIDTH, HEIGHT, outputFormat, threads);
                            PreparedOverlay overlay(WIDTH, HEIGHT);
                            auto actual = Allocate(outputFormat, WIDTH, HEIGHT);
                            for (int mode = 0; mode < 5; ++mode)
                            {
                                const auto layer = Layer(WIDTH, HEIGHT, mode);
                                const auto referenceWhite = mode == 4 ? 100.0 : 203.0;
                                Check(reference.Convert(source.get(), layer, color, referenceWhite, expected.get()), "legacy convert");
                                Convert(pipeline, source.get(), layer, color, referenceWhite, actual.get());
                                const auto context = std::string(av_get_pix_fmt_name(format)) + " -> " +
                                    av_get_pix_fmt_name(outputFormat) + " range=" + std::to_string(range) +
                                    " transfer=" + std::to_string(transfer) + " layout=" + std::to_string(layout) +
                                    " threads=" + std::to_string(threads) + " overlay=" + std::to_string(mode);
                                EqualSamples(expected.get(), actual.get(), context);
                                const an_export_overlay_info info{sizeof(info), 6, AN_EXPORT_OVERLAY_UPDATED, 0,
                                    static_cast<uint64_t>(mode + 1)};
                                overlay.Invalidate();
                                overlay.Accept(info, layer, color, referenceWhite, []() {});
                                Check(pipeline.Upsample(source.get()), "prepared overlay upsample");
                                pipeline.Composite(source.get(), overlay, color, referenceWhite, []() {});
                                Check(pipeline.Downsample(actual.get()), "prepared overlay downsample");
                                EqualSamples(expected.get(), actual.get(), context + " prepared foreground");
                                Require(originalBuffers == SnapshotBuffers(source.get()), context + ": source samples or padding were modified");
                                Require(originalFacts == SnapshotFacts(source.get()), context + ": source layout, crop or VFR timestamp facts were modified");
                                comparisons += 2;
                            }
                        }
                    }
                }
            }
        }
    }
    return comparisons;
}

size_t TallCroppedFramesMatchLegacyAcrossSliceBoundaries()
{
    constexpr int VISIBLE_WIDTH = 126;
    constexpr int VISIBLE_HEIGHT = 136;
    size_t comparisons = 0;
    for (const auto format : {AV_PIX_FMT_P010LE, AV_PIX_FMT_YUV420P10LE})
    {
        for (const auto transfer : {AVCOL_TRC_BT709, AVCOL_TRC_SMPTE2084, AVCOL_TRC_ARIB_STD_B67})
        {
            auto source = Source(format, AVCOL_RANGE_MPEG, transfer, 1, 0, VISIBLE_WIDTH, VISIBLE_HEIGHT);
            Require(source->width == 129 && source->height == 139, "Tall crop fixture has unexpected coded dimensions");
            const ColorPipeline color(source->colorspace, source->color_primaries, source->color_trc);
            for (const auto outputFormat : {AV_PIX_FMT_YUV420P, AV_PIX_FMT_NV12,
                AV_PIX_FMT_YUV420P10LE, AV_PIX_FMT_P010LE})
            {
                LegacyYuvFramePipeline reference(source.get(), VISIBLE_WIDTH, VISIBLE_HEIGHT, outputFormat);
                auto expected = Allocate(outputFormat, VISIBLE_WIDTH, VISIBLE_HEIGHT);
                for (const auto threads : {1, 4})
                {
                    YuvFramePipeline pipeline(source.get(), VISIBLE_WIDTH, VISIBLE_HEIGHT, outputFormat, threads);
                    PreparedOverlay overlay(VISIBLE_WIDTH, VISIBLE_HEIGHT);
                    auto actual = Allocate(outputFormat, VISIBLE_WIDTH, VISIBLE_HEIGHT);
                    for (const auto origin : {std::array<size_t, 2>{1, 1}, std::array<size_t, 2>{2, 0}, std::array<size_t, 2>{0, 2}})
                    {
                        source->crop_left = origin[0];
                        source->crop_top = origin[1];
                        source->crop_right = 3 - origin[0];
                        source->crop_bottom = 3 - origin[1];
                        const auto originalBuffers = SnapshotBuffers(source.get());
                        const auto originalFacts = SnapshotFacts(source.get());
                        for (const auto mode : {2, 3})
                        {
                            const auto layer = Layer(VISIBLE_WIDTH, VISIBLE_HEIGHT, mode);
                            Check(reference.Convert(source.get(), layer, color, 203, expected.get()), "legacy tall cropped convert");
                            Convert(pipeline, source.get(), layer, color, 203, actual.get());
                            const auto context = std::string("tall ") + av_get_pix_fmt_name(format) + " -> " +
                                av_get_pix_fmt_name(outputFormat) + " transfer=" + std::to_string(transfer) +
                                " threads=" + std::to_string(threads) + " crop=(" + std::to_string(origin[0]) +
                                "," + std::to_string(origin[1]) + ") overlay=" + std::to_string(mode);
                            EqualSamples(expected.get(), actual.get(), context);
                            const an_export_overlay_info info{sizeof(info), 6, AN_EXPORT_OVERLAY_UPDATED, 0,
                                static_cast<uint64_t>(mode)};
                            overlay.Invalidate();
                            overlay.Accept(info, layer, color, 203, []() {});
                            Check(pipeline.Upsample(source.get()), "prepared tall cropped upsample");
                            pipeline.Composite(source.get(), overlay, color, 203, []() {});
                            Check(pipeline.Downsample(actual.get()), "prepared tall cropped downsample");
                            EqualSamples(expected.get(), actual.get(), context + " prepared foreground");
                            Require(originalBuffers == SnapshotBuffers(source.get()) && originalFacts == SnapshotFacts(source.get()),
                                context + ": source changed while reusing a pipeline across crop origins");
                            comparisons += 2;
                        }
                    }
                }
            }
        }
    }
    return comparisons;
}

void ReuseOverwritesPreviouslyCompositedPixels()
{
    auto source = Source(AV_PIX_FMT_P010LE, AVCOL_RANGE_MPEG, AVCOL_TRC_SMPTE2084, 1);
    const ColorPipeline color(source->colorspace, source->color_primaries, source->color_trc);
    LegacyYuvFramePipeline reference(source.get(), WIDTH, HEIGHT, AV_PIX_FMT_P010LE);
    YuvFramePipeline pipeline(source.get(), WIDTH, HEIGHT, AV_PIX_FMT_P010LE, 3);
    auto expected = Allocate(AV_PIX_FMT_P010LE, WIDTH, HEIGHT);
    auto actual = Allocate(AV_PIX_FMT_P010LE, WIDTH, HEIGHT);
    for (int frame = 0; frame < 6; ++frame)
    {
        FillSource(source.get(), frame);
        source->pts = 3100 + frame * 41 + frame / 2 * 17;
        source->duration = frame % 2 ? 58 : 41;
        const auto snapshot = SnapshotBuffers(source.get());
        const auto facts = SnapshotFacts(source.get());
        const auto layer = Layer(WIDTH, HEIGHT, frame % 3 == 0 ? 0 : frame % 3 == 1 ? 2 : 3);
        Check(reference.Convert(source.get(), layer, color, 406, expected.get()), "legacy reuse");
        Convert(pipeline, source.get(), layer, color, 406, actual.get());
        EqualSamples(expected.get(), actual.get(), "multi-frame reused pipeline");
        Require(snapshot == SnapshotBuffers(source.get()) && facts == SnapshotFacts(source.get()), "Reused pipeline changed its source");
    }
}

void PreparedReuseUpdatesBackgroundAndEmptyIgnoresStalePixels()
{
    for (const auto transfer : {AVCOL_TRC_BT709, AVCOL_TRC_SMPTE2084, AVCOL_TRC_ARIB_STD_B67})
    {
        auto source = Source(AV_PIX_FMT_P010LE, AVCOL_RANGE_MPEG, transfer, 1);
        const ColorPipeline color(source->colorspace, source->color_primaries, source->color_trc);
        LegacyYuvFramePipeline reference(source.get(), WIDTH, HEIGHT, AV_PIX_FMT_P010LE);
        YuvFramePipeline pipeline(source.get(), WIDTH, HEIGHT, AV_PIX_FMT_P010LE, 4);
        PreparedOverlay overlay(WIDTH, HEIGHT);
        auto expected = Allocate(AV_PIX_FMT_P010LE, WIDTH, HEIGHT);
        auto actual = Allocate(AV_PIX_FMT_P010LE, WIDTH, HEIGHT);
        auto pixels = Layer(WIDTH, HEIGHT, 2);
        const std::vector<float> empty(pixels.size());
        for (int frame = 0; frame < 6; ++frame)
        {
            FillSource(source.get(), frame * 3);
            source->crop_left = frame % 2;
            source->crop_top = (frame + 1) % 2;
            source->crop_right = 3 - source->crop_left;
            source->crop_bottom = 3 - source->crop_top;
            source->pts += 71 + frame;
            source->duration = frame % 2 ? 40 : 71;
            const auto originalBuffers = SnapshotBuffers(source.get());
            const auto originalFacts = SnapshotFacts(source.get());
            const auto state = frame == 0 || frame == 4 ? AN_EXPORT_OVERLAY_UPDATED :
                frame == 2 ? AN_EXPORT_OVERLAY_EMPTY : AN_EXPORT_OVERLAY_UNCHANGED;
            const auto revision = static_cast<uint64_t>(frame < 2 ? 1 : frame < 4 ? 2 : 3);
            if (frame == 4)
            {
                pixels = Layer(WIDTH, HEIGHT, 3);
                for (size_t index = 0; index < pixels.size(); index += 4)
                {
                    if (pixels[index + 3] == 0)
                    {
                        pixels[index] = std::numeric_limits<float>::quiet_NaN();
                        pixels[index + 1] = std::numeric_limits<float>::infinity();
                        pixels[index + 3] = -0.0f;
                    }
                }
            }
            const an_export_overlay_info info{sizeof(info), 6, static_cast<uint32_t>(state), 0, revision};
            overlay.Accept(info, pixels, color, 406, []() {});
            Check(reference.Convert(source.get(), frame == 2 || frame == 3 ? empty : pixels,
                color, 406, expected.get()), "legacy revision reuse");
            Check(pipeline.Upsample(source.get()), "prepared revision reuse upsample");
            pipeline.Composite(source.get(), overlay, color, 406, []() {});
            Check(pipeline.Downsample(actual.get()), "prepared revision reuse downsample");
            EqualSamples(expected.get(), actual.get(), "prepared revision uses fresh background/crop and handles Empty");
            Require(originalBuffers == SnapshotBuffers(source.get()) && originalFacts == SnapshotFacts(source.get()),
                "Prepared overlay modified source facts while retaining its foreground");
        }
    }
}

void AdaptiveUpdatedFramesPreserveScalarValidationAndCancelRecovery()
{
    auto source = Source(AV_PIX_FMT_P010LE, AVCOL_RANGE_MPEG, AVCOL_TRC_ARIB_STD_B67, 1);
    const ColorPipeline color(source->colorspace, source->color_primaries, source->color_trc);
    LegacyYuvFramePipeline reference(source.get(), WIDTH, HEIGHT, AV_PIX_FMT_P010LE);
    YuvFramePipeline pipeline(source.get(), WIDTH, HEIGHT, AV_PIX_FMT_P010LE, 4);
    PreparedOverlay overlay(WIDTH, HEIGHT);
    auto pixels = Layer(WIDTH, HEIGHT, 2);
    auto expected = Allocate(AV_PIX_FMT_P010LE, WIDTH, HEIGHT);
    auto actual = Allocate(AV_PIX_FMT_P010LE, WIDTH, HEIGHT);
    const auto accept = [&](uint32_t state, uint64_t revision)
    {
        const an_export_overlay_info info{sizeof(info), 6, state, 0, revision};
        overlay.Accept(info, pixels, color, 203, []() {});
    };
    for (int frame = 0; frame < 3; ++frame)
    {
        FillSource(source.get(), frame);
        pixels[0] = 0.25f / (frame + 1);
        accept(AN_EXPORT_OVERLAY_UPDATED, frame + 1);
        Require(overlay.CoverageKnown() == (frame == 0), "Animated updated frame did not use the adaptive route");
        Check(reference.Convert(source.get(), pixels, color, 203, expected.get()), "legacy adaptive frame");
        Check(pipeline.Upsample(source.get()), "adaptive upsample");
        pipeline.Composite(source.get(), overlay, color, 203, []() {});
        Check(pipeline.Downsample(actual.get()), "adaptive downsample");
        EqualSamples(expected.get(), actual.get(), "animated adaptive scalar parity");
    }
    accept(AN_EXPORT_OVERLAY_UNCHANGED, 3);
    Require(overlay.CoverageKnown(), "Stable animation did not prepare retained foreground");
    for (const auto invalid : {-0.125f, 1.125f, std::numeric_limits<float>::quiet_NaN()})
    {
        pixels.back() = 0.5f;
        overlay.Invalidate();
        accept(AN_EXPORT_OVERLAY_UPDATED, 4);
        pixels.back() = invalid;
        accept(AN_EXPORT_OVERLAY_UPDATED, 5);
        Check(pipeline.Upsample(source.get()), "adaptive invalid alpha upsample");
        auto failed = false;
        try
        {
            pipeline.Composite(source.get(), overlay, color, 203, []() {});
        }
        catch (const std::invalid_argument &)
        {
            failed = true;
        }
        Require(failed && !overlay.IsValid() && pipeline.Downsample(actual.get()) == AVERROR(EINVAL),
            "Adaptive parallel validation accepted an illegal active alpha or retained its revision");
    }
    pixels.back() = 0.5f;
    accept(AN_EXPORT_OVERLAY_UPDATED, 6);
    accept(AN_EXPORT_OVERLAY_UPDATED, 7);
    Check(pipeline.Upsample(source.get()), "adaptive cancelled upsample");
    std::atomic<int> checks{0};
    auto cancelled = false;
    try
    {
        pipeline.Composite(source.get(), overlay, color, 203, [&]()
        {
            if (checks.fetch_add(1) >= 1) throw CoreError(ErrorCode::Cancelled, "adaptive cancel");
        });
    }
    catch (const CoreError &error)
    {
        cancelled = error.Code() == ErrorCode::Cancelled;
    }
    Require(cancelled && !overlay.IsValid() && pipeline.Downsample(actual.get()) == AVERROR(EINVAL),
        "Adaptive cancellation published a partial frame or reusable revision");
    accept(AN_EXPORT_OVERLAY_UPDATED, 8);
    Check(reference.Convert(source.get(), pixels, color, 203, expected.get()), "legacy adaptive recovery");
    Check(pipeline.Upsample(source.get()), "adaptive recovery upsample");
    pipeline.Composite(source.get(), overlay, color, 203, []() {});
    Check(pipeline.Downsample(actual.get()), "adaptive recovery downsample");
    EqualSamples(expected.get(), actual.get(), "adaptive cancellation recovery");
}

void PreparedColorBudgetFallbackMatchesLegacy()
{
    constexpr int VISIBLE_WIDTH = 1152;
    auto source = Source(AV_PIX_FMT_NV12, AVCOL_RANGE_JPEG, AVCOL_TRC_IEC61966_2_1, 2, 0, VISIBLE_WIDTH, HEIGHT);
    const ColorPipeline color(source->colorspace, source->color_primaries, source->color_trc);
    auto pixels = Layer(VISIBLE_WIDTH, HEIGHT, 2);
    for (size_t index = 0; index < pixels.size(); index += 4)
    {
        pixels[index] = static_cast<float>(index / 4) / 100000;
    }
    PreparedOverlay overlay(VISIBLE_WIDTH, HEIGHT);
    const an_export_overlay_info info{sizeof(info), 6, AN_EXPORT_OVERLAY_UPDATED, 0, 1};
    overlay.Accept(info, pixels, color, 203, []() {});
    Require(!overlay.PreparedPixels() && overlay.ActivePixels() == static_cast<uint64_t>(VISIBLE_WIDTH) * HEIGHT,
        "Fallback fixture did not exceed its foreground color budget");
    LegacyYuvFramePipeline reference(source.get(), VISIBLE_WIDTH, HEIGHT, AV_PIX_FMT_NV12);
    YuvFramePipeline pipeline(source.get(), VISIBLE_WIDTH, HEIGHT, AV_PIX_FMT_NV12, 4);
    auto expected = Allocate(AV_PIX_FMT_NV12, VISIBLE_WIDTH, HEIGHT);
    auto actual = Allocate(AV_PIX_FMT_NV12, VISIBLE_WIDTH, HEIGHT);
    Check(reference.Convert(source.get(), pixels, color, 203, expected.get()), "legacy foreground fallback");
    Check(pipeline.Upsample(source.get()), "prepared foreground fallback upsample");
    pipeline.Composite(source.get(), overlay, color, 203, []() {});
    Check(pipeline.Downsample(actual.get()), "prepared foreground fallback downsample");
    EqualSamples(expected.get(), actual.get(), "prepared color budget fallback");
}

void ClampedPipelineBudgetsMatchLegacy()
{
    auto source = Source(AV_PIX_FMT_NV12, AVCOL_RANGE_JPEG, AVCOL_TRC_IEC61966_2_1, 1);
    const ColorPipeline color(source->colorspace, source->color_primaries, source->color_trc);
    const auto layer = Layer(WIDTH, HEIGHT, 2);
    const auto originalBuffers = SnapshotBuffers(source.get());
    const auto originalFacts = SnapshotFacts(source.get());
    LegacyYuvFramePipeline reference(source.get(), WIDTH, HEIGHT, AV_PIX_FMT_YUV420P);
    auto expected = Allocate(AV_PIX_FMT_YUV420P, WIDTH, HEIGHT);
    Check(reference.Convert(source.get(), layer, color, 203, expected.get()), "legacy clamped thread budget");
    for (const auto threads : {-1, 0, 100})
    {
        YuvFramePipeline pipeline(source.get(), WIDTH, HEIGHT, AV_PIX_FMT_YUV420P, threads);
        auto actual = Allocate(AV_PIX_FMT_YUV420P, WIDTH, HEIGHT);
        Convert(pipeline, source.get(), layer, color, 203, actual.get());
        EqualSamples(expected.get(), actual.get(), "clamped pipeline threads=" + std::to_string(threads));
        Require(originalBuffers == SnapshotBuffers(source.get()) && originalFacts == SnapshotFacts(source.get()),
            "Clamped pipeline budget changed its source");
    }
}

void CancelledAndFailedCompositionRequireFreshUpsample()
{
    for (const auto threads : {1, 2, 4})
    {
        auto source = Source(AV_PIX_FMT_YUV420P10LE, AVCOL_RANGE_MPEG, AVCOL_TRC_ARIB_STD_B67, 1);
        const auto original = SnapshotBuffers(source.get());
        const ColorPipeline color(source->colorspace, source->color_primaries, source->color_trc);
        const auto layer = Layer(WIDTH, HEIGHT, 2);
        LegacyYuvFramePipeline reference(source.get(), WIDTH, HEIGHT, AV_PIX_FMT_YUV420P10LE);
        YuvFramePipeline pipeline(source.get(), WIDTH, HEIGHT, AV_PIX_FMT_YUV420P10LE, threads);
        auto expected = Allocate(AV_PIX_FMT_YUV420P10LE, WIDTH, HEIGHT);
        auto actual = Allocate(AV_PIX_FMT_YUV420P10LE, WIDTH, HEIGHT);
        Check(reference.Convert(source.get(), layer, color, 203, expected.get()), "legacy recovery");
        Check(pipeline.Upsample(source.get()), "upsample before cancellation");
        std::atomic<int> checks{0};
        auto cancelled = false;
        try
        {
            pipeline.Composite(source.get(), layer, color, 203, [&]()
            {
                if (checks.fetch_add(1) >= 1)
                {
                    throw CoreError(ErrorCode::Cancelled, "controlled pipeline cancellation");
                }
            });
        }
        catch (const CoreError &error)
        {
            Require(error.Code() == ErrorCode::Cancelled, "Pipeline cancellation lost its error classification");
            cancelled = true;
        }
        Require(cancelled, "Pipeline did not observe cancellation after entering multiple row chunks");
        Require(pipeline.Downsample(actual.get()) == AVERROR(EINVAL), "Cancelled partial composition was published");
        Convert(pipeline, source.get(), layer, color, 203, actual.get());
        EqualSamples(expected.get(), actual.get(), "cancelled pipeline recovery");

        for (const auto invalidAlpha : {-0.125f, 1.125f, std::numeric_limits<float>::quiet_NaN()})
        {
            auto invalid = layer;
            invalid.back() = invalidAlpha;
            Check(pipeline.Upsample(source.get()), "upsample before bad alpha");
            auto failed = false;
            try
            {
                pipeline.Composite(source.get(), invalid, color, 203, []() {});
            }
            catch (const std::invalid_argument &)
            {
                failed = true;
            }
            Require(failed, "Invalid overlay alpha was accepted");
            Require(pipeline.Downsample(actual.get()) == AVERROR(EINVAL), "Failed partial composition was published");
            Convert(pipeline, source.get(), layer, color, 203, actual.get());
            EqualSamples(expected.get(), actual.get(), "failed pipeline recovery");
        }
        Check(pipeline.Upsample(source.get()), "upsample before incomplete layer");
        auto rejectedIncompleteLayer = false;
        try
        {
            pipeline.Composite(source.get(), std::span<const float>(layer.data(), layer.size() - 1), color, 203, []() {});
        }
        catch (const std::invalid_argument &)
        {
            rejectedIncompleteLayer = true;
        }
        Require(rejectedIncompleteLayer, "Incomplete overlay layer was accepted");
        Require(pipeline.Downsample(actual.get()) == AVERROR(EINVAL), "Incomplete layer left an uncomposited frame ready");
        Convert(pipeline, source.get(), layer, color, 203, actual.get());
        EqualSamples(expected.get(), actual.get(), "incomplete overlay recovery");
        Require(original == SnapshotBuffers(source.get()), "Failed/cancelled composition modified the original input");
        source->crop_right++;
        Require(pipeline.Upsample(source.get()) == AVERROR(EINVAL), "Invalid visible crop was accepted");
        Require(pipeline.Downsample(actual.get()) == AVERROR(EINVAL), "Invalid crop left a previous frame ready");
        source->crop_right--;
        Convert(pipeline, source.get(), layer, color, 203, actual.get());
        EqualSamples(expected.get(), actual.get(), "invalid crop recovery");
    }
}

void ExecutorVisitsEachRowOnceWithinItsBudget()
{
    for (const auto requested : {size_t{0}, size_t{1}, size_t{2}, size_t{4}, size_t{100}})
    {
        const auto budget = std::clamp<size_t>(requested, 1, std::min<size_t>(4, std::max(1U, std::thread::hardware_concurrency())));
        FrameRowExecutor executor(requested);
        std::set<std::thread::id> threads;
        std::mutex mutex;
        for (const auto rows : {size_t{0}, size_t{1}, size_t{31}, size_t{32}, size_t{33}, size_t{97}})
        {
            std::array<std::atomic<int>, 128> visits{};
            executor.Execute(rows, [&](size_t begin, size_t end)
            {
                Require(begin < end && end <= rows && end - begin <= 32 && begin % 32 == 0, "Executor emitted an invalid row range");
                {
                    std::scoped_lock lock(mutex);
                    threads.insert(std::this_thread::get_id());
                }
                for (auto row = begin; row < end; ++row)
                {
                    visits[row].fetch_add(1);
                }
            });
            for (size_t row = 0; row < visits.size(); ++row)
            {
                Require(visits[row].load() == (row < rows ? 1 : 0), "Executor skipped or duplicated a row");
            }
        }
        Require(threads.size() <= budget, "Executor exceeded its caller-inclusive persistent thread budget");
    }
}

void ExecutorWorkersParticipateAndJoinBeforeRethrowing()
{
    const auto budget = std::min<size_t>(4, std::max(1U, std::thread::hardware_concurrency()));
    if (budget < 2)
    {
        return;
    }
    FrameRowExecutor executor(budget);
    for (int repetition = 0; repetition < 3; ++repetition)
    {
        std::mutex mutex;
        std::condition_variable changed;
        size_t entered = 0;
        size_t active = 0;
        bool throwing = false;
        bool releaseOthers = false;
        std::set<std::thread::id> participants;
        auto controller = std::thread([&]()
        {
            std::unique_lock lock(mutex);
            changed.wait(lock, [&]() { return throwing; });
            releaseOthers = true;
            changed.notify_all();
        });
        auto failed = false;
        size_t activeAtReturn = 0;
        std::exception_ptr unexpected;
        try
        {
            executor.Execute(budget * 32, [&](size_t begin, size_t)
            {
                std::unique_lock lock(mutex);
                ++entered;
                ++active;
                participants.insert(std::this_thread::get_id());
                changed.notify_all();
                if (!changed.wait_for(lock, std::chrono::seconds(5), [&]() { return entered == budget; }))
                {
                    --active;
                    throwing = true;
                    changed.notify_all();
                    throw std::runtime_error("Executor failed to start every bounded worker");
                }
                if (begin == 0)
                {
                    --active;
                    throwing = true;
                    changed.notify_all();
                    throw std::runtime_error("controlled executor failure");
                }
                changed.wait(lock, [&]() { return releaseOthers; });
                --active;
            });
        }
        catch (const std::runtime_error &error)
        {
            failed = std::string(error.what()) == "controlled executor failure";
            if (!failed)
            {
                unexpected = std::current_exception();
            }
        }
        catch (...)
        {
            unexpected = std::current_exception();
        }
        {
            std::scoped_lock lock(mutex);
            activeAtReturn = active;
            releaseOthers = true;
            throwing = true;
            changed.notify_all();
        }
        controller.join();
        if (unexpected)
        {
            std::rethrow_exception(unexpected);
        }
        Require(failed, "Executor did not rethrow the worker failure");
        Require(activeAtReturn == 0, "Executor rethrew before all callback actions exited");
        Require(participants.size() == budget && participants.contains(std::this_thread::get_id()),
            "Executor did not use every persistent worker and the caller");
        std::array<std::atomic<int>, 97> visits{};
        executor.Execute(visits.size(), [&](size_t begin, size_t end)
        {
            for (auto row = begin; row < end; ++row)
            {
                visits[row].fetch_add(1);
            }
        });
        for (const auto &visit : visits)
        {
            Require(visit.load() == 1, "Executor could not be reused after a worker exception");
        }
    }
}

void SourceAlphaUsesLinearMatteBeforeSubtitleComposition()
{
    for (const auto transfer : {AVCOL_TRC_BT709, AVCOL_TRC_SMPTE2084, AVCOL_TRC_ARIB_STD_B67})
    {
        auto source = Source(AV_PIX_FMT_YUVA444P12LE, AVCOL_RANGE_MPEG, transfer, 0, 0, 6, 4);
        source->alpha_mode = AVALPHA_MODE_STRAIGHT;
        const auto *descriptor = av_pix_fmt_desc_get(static_cast<AVPixelFormat>(source->format));
        const auto maximum = (1u << descriptor->comp[3].depth) - 1;
        for (int y = 0; y < source->height; ++y)
        {
            for (int x = 0; x < source->width; ++x)
            {
                reinterpret_cast<uint16_t *>(source->data[0] + y * source->linesize[0])[x] = 3012;
                reinterpret_cast<uint16_t *>(source->data[1] + y * source->linesize[1])[x] = 1944;
                reinterpret_cast<uint16_t *>(source->data[2] + y * source->linesize[2])[x] = 2232;
                reinterpret_cast<uint16_t *>(source->data[3] + y * source->linesize[3])[x] = static_cast<uint16_t>((x / 2) * maximum / 2);
            }
        }
        const auto original = SnapshotBuffers(source.get());
        const auto facts = SnapshotFacts(source.get());
        ColorPipeline color(source->colorspace, source->color_primaries, source->color_trc);
        YuvFramePipeline pipeline(source.get(), 6, 4, AV_PIX_FMT_YUV444P16LE);
        auto output = Allocate(AV_PIX_FMT_YUV444P16LE, 6, 4);
        auto checks = 0;
        auto cancelled = false;
        try
        {
            pipeline.Upsample(source.get(), [&]()
            {
                if (++checks == 2)
                {
                    throw CoreError(ErrorCode::Cancelled, "controlled export alpha cancellation");
                }
            });
        }
        catch (const CoreError &error)
        {
            cancelled = error.Code() == ErrorCode::Cancelled;
        }
        Require(cancelled && pipeline.Downsample(output.get()) < 0 && original == SnapshotBuffers(source.get()),
            "Export alpha cancellation did not invalidate partial output or preserved raw source incorrectly");
        for (const auto overlayAlpha : {0.0f, 0.4f})
        {
            std::vector<float> layer(6 * 4 * 4);
            const std::array<float, 4> foreground{0.1f * overlayAlpha, 0.25f * overlayAlpha, 0.05f * overlayAlpha, overlayAlpha};
            for (size_t offset = 0; offset < layer.size(); offset += 4)
            {
                std::copy(foreground.begin(), foreground.end(), layer.begin() + offset);
            }
            Check(pipeline.Upsample(source.get()), "alpha pipeline upsample");
            pipeline.Composite(source.get(), layer, color, 203, []() {});
            Check(pipeline.Downsample(output.get()), "alpha pipeline downsample");
            for (int x = 0; x < source->width; ++x)
            {
                auto expected = color.Decode({(3012.0 * 16 - 4096) / 56064,
                    (1944.0 * 16 - 32768) / 57344, (2232.0 * 16 - 32768) / 57344});
                const auto alpha = static_cast<double>((x / 2) * maximum / 2) / maximum;
                for (auto &component : expected)
                {
                    component *= alpha;
                }
                const auto matte = color.Encode(expected);
                expected = color.Composite(matte, foreground, 203);
                for (int plane = 0; plane < 3; ++plane)
                {
                    const auto reference = expected[plane] * (plane ? 57344 : 56064) + (plane ? 32768 : 4096);
                    const auto actual = reinterpret_cast<const uint16_t *>(output->data[plane])[x];
                    Require(std::abs(actual - reference) <= 3, "Export alpha matte or subsequent subtitle differs from high-precision source-domain reference");
                }
            }
            Require(original == SnapshotBuffers(source.get()) && facts == SnapshotFacts(source.get()) &&
                source->alpha_mode == AVALPHA_MODE_STRAIGHT, "Export source alpha bytes or metadata were changed");
        }
    }
}
}

int main()
{
    try
    {
        auto comparisons = LegacyMatrixParity();
        comparisons += TallCroppedFramesMatchLegacyAcrossSliceBoundaries();
        ReuseOverwritesPreviouslyCompositedPixels();
        PreparedReuseUpdatesBackgroundAndEmptyIgnoresStalePixels();
        AdaptiveUpdatedFramesPreserveScalarValidationAndCancelRecovery();
        PreparedColorBudgetFallbackMatchesLegacy();
        ClampedPipelineBudgetsMatchLegacy();
        CancelledAndFailedCompositionRequireFreshUpsample();
        ExecutorVisitsEachRowOnceWithinItsBudget();
        ExecutorWorkersParticipateAndJoinBeforeRethrowing();
        SourceAlphaUsesLinearMatteBeforeSubtitleComposition();
        std::cout << "PASS " << comparisons << " bit-exact legacy comparisons: NV12/P010/4208/42010/44416, full/limited, "
            "SDR/PQ/HLG, crop/negative stride/padding, alpha, reuse, cancellation, exceptions and bounded row workers\n";
        return 0;
    }
    catch (const std::exception &error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}
