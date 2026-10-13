#include "export_muxer.h"
#include "export_error.h"
#include <algorithm>
#include <cctype>
#include <cstdlib>
#include <filesystem>
#include <memory>
#include <string>
extern "C"
{
#include <libavutil/mem.h>
#include <libavutil/intreadwrite.h>
}
namespace aeginext::encode
{
namespace
{
int Interrupt(void *value) { return static_cast<const std::atomic<bool> *>(value)->load() ? 1 : 0; }
struct Input
{
    AVFormatContext *format = nullptr;
    AVPacket *packet = av_packet_alloc();
    int selected = -1;
    bool ended = false;
    uint64_t packetCount = 0;
    int64_t firstPts = AV_NOPTS_VALUE, lastEnd = AV_NOPTS_VALUE, lastDuration = 0;
    uint64_t skipSamples = 0, discardSamples = 0;
    uint64_t presentationHash = 14695981039346656037ull;
    AVRational comparisonTimeBase{0,1};
    ~Input() { av_packet_free(&packet); avformat_close_input(&format); }
    void Open(const char *path, const std::atomic<bool> &cancelled)
    {
        if (!packet) throw std::bad_alloc();
        format = avformat_alloc_context();
        if (!format) throw std::bad_alloc();
        format->interrupt_callback = {Interrupt, const_cast<std::atomic<bool> *>(&cancelled)};
        CheckAv(avformat_open_input(&format, path, nullptr, nullptr), "open mux input");
        CheckAv(avformat_find_stream_info(format, nullptr), "read mux input streams");
    }
    bool Next()
    {
        av_packet_unref(packet);
        while (!ended)
        {
            const auto result = av_read_frame(format, packet);
            if (result == AVERROR_EOF) { ended = true; return false; }
            CheckAv(result, "read mux packet");
            if (packet->stream_index == selected)
            {
                if (!packetCount) firstPts = packet->pts;
                ++packetCount;
                lastDuration = packet->duration;
                const bool video = format->streams[selected]->codecpar->codec_type == AVMEDIA_TYPE_VIDEO;
                if (packet->pts != AV_NOPTS_VALUE) lastEnd = std::max(lastEnd, packet->pts + (video ? 0 : packet->duration));
                if (video && comparisonTimeBase.num > 0)
                {
                    const auto timestamp = packet->pts == AV_NOPTS_VALUE ? AV_NOPTS_VALUE :
                        av_rescale_q(packet->pts, format->streams[selected]->time_base, comparisonTimeBase);
                    presentationHash ^= static_cast<uint64_t>(timestamp);
                    presentationHash *= 1099511628211ull;
                }
                size_t size = 0;
                const auto *skip = av_packet_get_side_data(packet, AV_PKT_DATA_SKIP_SAMPLES, &size);
                if (skip && size >= 10) { skipSamples += AV_RL32(skip); discardSamples += AV_RL32(skip + 4); }
                return true;
            }
            av_packet_unref(packet);
        }
        return false;
    }
};
struct Output
{
    AVFormatContext *format = nullptr;
    ~Output()
    {
        if (!format) return;
        if (format->pb) avio_closep(&format->pb);
        avformat_free_context(format);
    }
};
void CopyChapters(const AVFormatContext *source, AVFormatContext *output)
{
    if (!source->nb_chapters) return;
    output->chapters = static_cast<AVChapter **>(av_calloc(source->nb_chapters, sizeof(*output->chapters)));
    if (!output->chapters) throw std::bad_alloc();
    for (unsigned i = 0; i < source->nb_chapters; ++i)
    {
        auto *chapter = static_cast<AVChapter *>(av_mallocz(sizeof(AVChapter)));
        if (!chapter) throw std::bad_alloc();
        output->chapters[output->nb_chapters++] = chapter;
        chapter->id = source->chapters[i]->id;
        chapter->time_base = source->chapters[i]->time_base;
        chapter->start = source->chapters[i]->start; chapter->end = source->chapters[i]->end;
        CheckAv(av_dict_copy(&chapter->metadata, source->chapters[i]->metadata, 0), "copy chapter metadata");
    }
}
AVStream *AddStream(AVFormatContext *output, const AVStream *source)
{
    auto *stream = avformat_new_stream(output, nullptr);
    if (!stream) throw std::bad_alloc();
    CheckAv(avcodec_parameters_copy(stream->codecpar, source->codecpar), "copy mux stream parameters");
    stream->codecpar->codec_tag = 0;
    stream->time_base = source->time_base; stream->avg_frame_rate = source->avg_frame_rate;
    stream->r_frame_rate = source->r_frame_rate; stream->sample_aspect_ratio = source->sample_aspect_ratio;
    stream->disposition = source->disposition;
    CheckAv(av_dict_copy(&stream->metadata, source->metadata, 0), "copy mux stream metadata");
    return stream;
}
uint64_t TimeBaseSamples(const AVStream *stream)
{
    if (stream->codecpar->sample_rate <= 0) return 0;
    return static_cast<uint64_t>(std::max<int64_t>(1,
        av_rescale_q_rnd(1, stream->time_base, AVRational{1, stream->codecpar->sample_rate}, AV_ROUND_UP)));
}
uint64_t EffectiveLeadingPadding(const Input &input, const AVStream *stream)
{
    return std::max(input.skipSamples,
        static_cast<uint64_t>(std::max(0, stream->codecpar->initial_padding)));
}
uint64_t EffectiveTrailingPadding(const Input &input, const AVStream *stream)
{
    uint64_t implicitPadding = 0;
    const auto *parameters = stream->codecpar;
    if (parameters->sample_rate > 0 && parameters->frame_size > 0 && input.lastDuration > 0)
    {
        const auto duration = av_rescale_q_rnd(input.lastDuration, stream->time_base,
            AVRational{1, parameters->sample_rate}, AV_ROUND_NEAR_INF);
        if (duration >= 0 && duration < parameters->frame_size)
            implicitPadding = static_cast<uint64_t>(parameters->frame_size - duration);
    }
    return std::max({input.discardSamples, implicitPadding,
        static_cast<uint64_t>(std::max(0, parameters->trailing_padding))});
}
uint64_t Difference(uint64_t left, uint64_t right)
{
    return left > right ? left - right : right - left;
}
void VerifyPackets(const Input &source, const char *path, AVMediaType type, const std::atomic<bool> &cancelled)
{
    Input actual;
    actual.Open(path, cancelled);
    actual.selected = av_find_best_stream(actual.format, type, -1, -1, nullptr, 0);
    CheckAv(actual.selected, "locate final verification stream");
    actual.comparisonTimeBase = source.comparisonTimeBase;
    while (actual.Next()) {}
    const auto *before = source.format->streams[source.selected]; const auto *after = actual.format->streams[actual.selected];
    Require(before->codecpar->codec_id == after->codecpar->codec_id && source.packetCount == actual.packetCount,
        "Final container changed the selected codec or packet count");
    const int64_t quantum = std::max({int64_t{1}, av_rescale_q(1, before->time_base, AV_TIME_BASE_Q),
        av_rescale_q(1, after->time_base, AV_TIME_BASE_Q)});
    const auto difference = [&](int64_t left, int64_t right)
    {
        if (left == AV_NOPTS_VALUE || right == AV_NOPTS_VALUE) return left == right ? int64_t{0} : INT64_MAX;
        return std::abs(av_rescale_q(left, before->time_base, AV_TIME_BASE_Q) - av_rescale_q(right, after->time_base, AV_TIME_BASE_Q));
    };
    if (difference(source.firstPts, actual.firstPts) > quantum || difference(source.lastEnd, actual.lastEnd) > quantum)
        throw ExportFailure(2, std::string("Final container changed ") + (type == AVMEDIA_TYPE_AUDIO ? "audio" : "video") +
            " timestamps: source first/lastPTS-or-audioEnd=" + std::to_string(source.firstPts) + "/" + std::to_string(source.lastEnd) +
            " timebase=" + std::to_string(before->time_base.num) + "/" + std::to_string(before->time_base.den) +
            "; output first/end=" + std::to_string(actual.firstPts) + "/" + std::to_string(actual.lastEnd) +
            " timebase=" + std::to_string(after->time_base.num) + "/" + std::to_string(after->time_base.den) +
            "; allowed microseconds=" + std::to_string(quantum));
    if (type == AVMEDIA_TYPE_VIDEO)
        Require(source.presentationHash == actual.presentationHash,
            "Final container changed one or more video presentation timestamps");
    if (type == AVMEDIA_TYPE_AUDIO)
    {
        Require(before->codecpar->sample_rate == after->codecpar->sample_rate,
            "Final container changed the selected audio sample rate");
        const auto sourceLeading = EffectiveLeadingPadding(source, before);
        const auto actualLeading = EffectiveLeadingPadding(actual, after);
        const auto sourceTrailing = EffectiveTrailingPadding(source, before);
        const auto actualTrailing = EffectiveTrailingPadding(actual, after);
        const auto sampleTolerance = std::max(TimeBaseSamples(before), TimeBaseSamples(after));
        if (Difference(sourceLeading, actualLeading) > sampleTolerance ||
            Difference(sourceTrailing, actualTrailing) > sampleTolerance)
            throw ExportFailure(2, "Final container did not preserve audio priming or discard padding: source initial/skip/discard=" +
                std::to_string(before->codecpar->initial_padding) + "/" + std::to_string(source.skipSamples) + "/" +
                std::to_string(source.discardSamples) + " frame/duration=" + std::to_string(before->codecpar->frame_size) + "/" +
                std::to_string(source.lastDuration) + "; output=" + std::to_string(after->codecpar->initial_padding) + "/" +
                std::to_string(actual.skipSamples) + "/" + std::to_string(actual.discardSamples) + " frame/duration=" +
                std::to_string(after->codecpar->frame_size) + "/" + std::to_string(actual.lastDuration) +
                "; effective leading/trailing=" + std::to_string(sourceLeading) + "/" + std::to_string(sourceTrailing) +
                " -> " + std::to_string(actualLeading) + "/" + std::to_string(actualTrailing) +
                "; tolerance=" + std::to_string(sampleTolerance) + " samples");
    }
}
void VerifyChapters(const AVFormatContext *source, const char *path, const std::atomic<bool> &cancelled)
{
    if (!source->nb_chapters) return;
    Input output;
    output.Open(path, cancelled);
    Require(output.format->nb_chapters == source->nb_chapters,
        "Final container did not preserve all source chapters; select Matroska for this source");
    for (unsigned i = 0; i < source->nb_chapters; ++i)
    {
        const auto *expected = source->chapters[i]; const auto *actual = output.format->chapters[i];
        const auto quantum = std::max<int64_t>(1, av_rescale_q(1, actual->time_base, AV_TIME_BASE_Q));
        const auto difference = [&](int64_t before, int64_t after)
        {
            return std::abs(av_rescale_q(before, expected->time_base, AV_TIME_BASE_Q) -
                av_rescale_q(after, actual->time_base, AV_TIME_BASE_Q));
        };
        Require(difference(expected->start, actual->start) <= quantum && difference(expected->end, actual->end) <= quantum,
            "Final container changed source chapter timing; select Matroska to preserve these chapters");
        const auto *before = av_dict_get(expected->metadata, "title", nullptr, 0);
        const auto *after = av_dict_get(actual->metadata, "title", nullptr, 0);
        Require(!before || (after && std::string(before->value) == after->value), "Final container lost a chapter title");
    }
}
int64_t Timestamp(const AVPacket *packet)
{
    return packet->dts != AV_NOPTS_VALUE ? packet->dts : packet->pts;
}
}
void MuxExport(const char *sourcePath, const char *videoPath, const an_export_mux_request &request,
    const ExportMetadata &metadata, const ExportGeometry &geometry, AVCodecID codec, int depth,
    const std::atomic<bool> &cancelled)
{
    const auto outputPath = std::filesystem::path(reinterpret_cast<const char8_t *>(request.output_path));
    Require(!std::filesystem::exists(outputPath), "Final export output already exists");
    auto extension = outputPath.extension().string();
    std::transform(extension.begin(), extension.end(), extension.begin(), [](unsigned char c) { return static_cast<char>(std::tolower(c)); });
    const char *container = extension == ".webm" ? "webm" : extension == ".mkv" ? "matroska" : extension == ".mp4" ? "mp4" : nullptr;
    Require(container != nullptr, "Final export container must be WebM, MKV or MP4");
    Require(codec != AV_CODEC_ID_VP8 || extension != ".mp4", "VP8 is not supported in MP4 export");
    Require(extension != ".webm" || codec == AV_CODEC_ID_VP8 || codec == AV_CODEC_ID_VP9 || codec == AV_CODEC_ID_AV1,
        "WebM requires VP8, VP9 or AV1 video");
    Input original, video, audio;
    original.Open(sourcePath, cancelled); video.Open(videoPath, cancelled);
    video.selected = av_find_best_stream(video.format, AVMEDIA_TYPE_VIDEO, -1, -1, nullptr, 0);
    CheckAv(video.selected, "locate encoded mux video");
    if (request.audio_path)
    {
        audio.Open(request.audio_path, cancelled);
        Require(request.audio_stream_index >= 0 && static_cast<unsigned>(request.audio_stream_index) < audio.format->nb_streams &&
            audio.format->streams[request.audio_stream_index]->codecpar->codec_type == AVMEDIA_TYPE_AUDIO,
            "Selected mux audio stream is unavailable");
        audio.selected = request.audio_stream_index;
    }
    Output output;
    CheckAv(avformat_alloc_output_context2(&output.format, nullptr, container, request.output_path), "create final mux container");
    output.format->interrupt_callback = {Interrupt, const_cast<std::atomic<bool> *>(&cancelled)};
    output.format->avoid_negative_ts = AVFMT_AVOID_NEG_TS_DISABLED;
    CheckAv(av_dict_copy(&output.format->metadata, original.format->metadata, 0), "copy source metadata");
    CopyChapters(original.format, output.format);
    auto *targetVideo = AddStream(output.format, video.format->streams[video.selected]);
    metadata.Apply(targetVideo->codecpar);
    AVStream *targetAudio = nullptr;
    if (audio.format)
    {
        auto *sourceAudio = audio.format->streams[audio.selected];
        const auto audioCodec = sourceAudio->codecpar->codec_id;
        Require(extension != ".webm" || audioCodec == AV_CODEC_ID_OPUS || audioCodec == AV_CODEC_ID_VORBIS,
            "WebM audio copy requires Opus or Vorbis; prepare Opus audio for other codecs");
        Require(avformat_query_codec(output.format->oformat, audioCodec, FF_COMPLIANCE_NORMAL) > 0,
            "Selected audio codec cannot be copied into the final container");
        targetAudio = AddStream(output.format, sourceAudio);
    }
    bool created = false;
    try
    {
        CheckAv(avio_open2(&output.format->pb, request.output_path, AVIO_FLAG_WRITE, &output.format->interrupt_callback, nullptr), "open final output");
        created = true;
        AVDictionary *options = nullptr;
        if (extension == ".mp4")
        {
            av_dict_set(&options, "movflags", "+faststart+write_colr", 0);
            av_dict_set(&options, "use_editlist", "1", 0);
        }
        const auto header = avformat_write_header(output.format, &options);
        av_dict_free(&options);
        CheckAv(header, "write final container header");
        video.comparisonTimeBase = targetVideo->time_base;
        bool hasVideo = video.Next(), hasAudio = audio.format && audio.Next();
        while (hasVideo || hasAudio)
        {
            if (cancelled.load()) throw ExportFailure(4, "Export cancelled");
            const bool writeVideo = hasVideo && (!hasAudio || Timestamp(audio.packet) == AV_NOPTS_VALUE ||
                (Timestamp(video.packet) != AV_NOPTS_VALUE && av_compare_ts(Timestamp(video.packet), video.format->streams[video.selected]->time_base,
                    Timestamp(audio.packet), audio.format->streams[audio.selected]->time_base) <= 0));
            auto &input = writeVideo ? video : audio;
            auto *target = writeVideo ? targetVideo : targetAudio;
            av_packet_rescale_ts(input.packet, input.format->streams[input.selected]->time_base, target->time_base);
            input.packet->stream_index = target->index; input.packet->pos = -1;
            CheckAv(av_interleaved_write_frame(output.format, input.packet), "mux final media packet");
            if (writeVideo) hasVideo = video.Next(); else hasAudio = audio.Next();
        }
        CheckAv(av_write_trailer(output.format), "write final container trailer");
        CheckAv(avio_closep(&output.format->pb), "close final output");
        metadata.Verify(request.output_path, codec, depth, geometry, true, cancelled);
        VerifyChapters(original.format, request.output_path, cancelled);
        VerifyPackets(video, request.output_path, AVMEDIA_TYPE_VIDEO, cancelled);
        if (audio.format) VerifyPackets(audio, request.output_path, AVMEDIA_TYPE_AUDIO, cancelled);
    }
    catch (...)
    {
        if (output.format->pb) avio_closep(&output.format->pb);
        if (created) { std::error_code ignored; std::filesystem::remove(outputPath, ignored); }
        throw;
    }
}
}
