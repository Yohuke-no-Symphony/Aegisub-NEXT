#include "aeginext_audio.h"
#include "audio_versions.h"
#include "audio_output.h"
#include <SDL3/SDL.h>
extern "C" {
#include <libavformat/avformat.h>
#include <libavcodec/avcodec.h>
#include <libavutil/avstring.h>
#include <libavutil/buffer.h>
#include <libavutil/channel_layout.h>
#include <libavutil/samplefmt.h>
#include <libavutil/version.h>
#include <libswresample/swresample.h>
}
#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <limits>
#include <memory>
#include <mutex>
#include <numeric>
#include <stdexcept>
#include <vector>

namespace {
constexpr int MAX_CONVERTED_FRAMES = 262144;
constexpr int SEEK_PREROLL_SECONDS = 1;
constexpr int TIMESTAMP_HARD_COMPENSATION_MILLISECONDS = 100;
void check(int result, const char *operation)
{
    if (result < 0)
    {
        char detail[AV_ERROR_MAX_STRING_SIZE]{};
        av_strerror(result, detail, sizeof(detail));
        throw std::runtime_error(std::string(operation) + ": " + detail);
    }
}
void require(bool condition, const char *message)
{
    if (!condition) { throw std::runtime_error(message); }
}
template<typename Function> int boundary(char *error, uint32_t capacity, Function &&function)
{
    if (error && capacity) { error[0] = 0; }
    try { return function(); }
    catch (const std::exception &exception)
    {
        if (error && capacity) { std::snprintf(error, capacity, "%s", exception.what()); }
        return 2;
    }
    catch (...)
    {
        if (error && capacity) { std::snprintf(error, capacity, "Unknown audio failure"); }
        return 2;
    }
}
void verify_versions()
{
    require(avformat_version() == AN_EXPECTED_AVFORMAT && LIBAVFORMAT_VERSION_INT == AN_EXPECTED_AVFORMAT &&
        avcodec_version() == AN_EXPECTED_AVCODEC && LIBAVCODEC_VERSION_INT == AN_EXPECTED_AVCODEC &&
        avutil_version() == AN_EXPECTED_AVUTIL && LIBAVUTIL_VERSION_INT == AN_EXPECTED_AVUTIL &&
        swresample_version() == AN_EXPECTED_SWRESAMPLE && LIBSWRESAMPLE_VERSION_INT == AN_EXPECTED_SWRESAMPLE,
        "FFmpeg audio build/runtime versions do not match the pinned manifest");
}
struct Decoder
{
    std::atomic_bool cancelled{false};
    AVFormatContext *format = nullptr;
    AVCodecContext *codec = nullptr;
    AVPacket *packet = av_packet_alloc();
    AVFrame *frame = av_frame_alloc();
    SwrContext *resampler = nullptr;
    int stream = -1;
    int rate = 0;
    int channels = 0;
    bool input_end = false;
    bool sent_end = false;
    bool resampler_end = false;
    bool packet_pending = false;
    bool uses_matroska_blocks = false;
    bool asynchronous_timestamps = false;
    int64_t first_packet_timestamp = AV_NOPTS_VALUE;
    int64_t last_block_position = -1;
    int64_t next_sample = AV_NOPTS_VALUE;
    int64_t resample_origin_input = AV_NOPTS_VALUE;
    int64_t resample_origin_output = AV_NOPTS_VALUE;
    bool align_seek_input = false;
    bool frame_held = false;
    int64_t next_input_sample = AV_NOPTS_VALUE;
    int64_t gap_input_samples = 0;
    int64_t minimum_sample = std::numeric_limits<int64_t>::min();
    int64_t pending_start = 0;
    int pending_offset = 0;
    std::vector<float> pending;
    std::vector<const uint8_t *> input_planes;
    ~Decoder()
    {
        swr_free(&resampler);
        av_frame_free(&frame);
        av_packet_free(&packet);
        avcodec_free_context(&codec);
        avformat_close_input(&format);
    }
    static int interrupt(void *context) { return static_cast<Decoder *>(context)->cancelled.load() ? 1 : 0; }
    void open(const char *path, int index, int output_rate, int output_channels)
    {
        verify_versions();
        require(path && !format && packet && frame && output_rate >= 8000 && output_rate <= 192000 &&
            output_channels >= 1 && output_channels <= 2, "Invalid audio open arguments");
        rate = output_rate;
        channels = output_channels;
        format = avformat_alloc_context();
        require(format != nullptr, "Audio format allocation failed");
        format->interrupt_callback = {interrupt, this};
        AVDictionary *options = nullptr;
        av_dict_set(&options, "protocol_whitelist", "file", 0);
        const auto result = avformat_open_input(&format, path, nullptr, &options);
        av_dict_free(&options);
        check(result, "Open audio");
        check(avformat_find_stream_info(format, nullptr), "Probe audio");
        require(index >= 0 && static_cast<unsigned>(index) < format->nb_streams &&
            format->streams[index]->codecpar->codec_type == AVMEDIA_TYPE_AUDIO, "Requested stream is not audio");
        stream = index;
        uses_matroska_blocks = av_match_name("matroska", format->iformat->name) ||
            av_match_name("webm", format->iformat->name);
        const auto *decoder = avcodec_find_decoder(format->streams[stream]->codecpar->codec_id);
        require(decoder != nullptr, "Audio codec unavailable");
        codec = avcodec_alloc_context3(decoder);
        require(codec != nullptr, "Audio codec allocation failed");
        check(avcodec_parameters_to_context(codec, format->streams[stream]->codecpar), "Audio codec parameters");
        codec->pkt_timebase = format->streams[stream]->time_base;
        codec->thread_count = 1;
        if (uses_matroska_blocks)
        {
            codec->flags |= AV_CODEC_FLAG_COPY_OPAQUE;
        }
        check(avcodec_open2(codec, decoder, nullptr), "Open audio codec");
        require(codec->sample_rate > 0 && codec->ch_layout.nb_channels > 0, "Audio format is incomplete");
        AVChannelLayout layout{};
        av_channel_layout_default(&layout, channels);
        const auto configured = swr_alloc_set_opts2(&resampler, &layout, AV_SAMPLE_FMT_FLT, rate,
            &codec->ch_layout, codec->sample_fmt, codec->sample_rate, 0, nullptr);
        av_channel_layout_uninit(&layout);
        check(configured, "Create audio resampler");
        check(swr_init(resampler), "Initialize audio resampler");
    }
    bool receive()
    {
        while (!cancelled.load())
        {
            av_frame_unref(frame);
            const auto result = avcodec_receive_frame(codec, frame);
            if (result == 0) { return true; }
            if (result == AVERROR_EOF) { return false; }
            if (result != AVERROR(EAGAIN) && result != AVERROR_INVALIDDATA) { check(result, "Decode audio"); }
            if (packet_pending)
            {
                const auto sent = avcodec_send_packet(codec, packet);
                if (sent == AVERROR(EAGAIN)) { throw std::runtime_error("Audio codec made no progress"); }
                if (sent != AVERROR_INVALIDDATA) { check(sent, "Send audio packet"); }
                av_packet_unref(packet);
                packet_pending = false;
                continue;
            }
            if (input_end)
            {
                require(!sent_end, "Audio decoder stalled while draining");
                check(avcodec_send_packet(codec, nullptr), "Drain audio codec");
                sent_end = true;
                continue;
            }
            while (true)
            {
                if (cancelled.load()) { return false; }
                const auto read = av_read_frame(format, packet);
                if (read == AVERROR_EOF) { input_end = true; break; }
                check(read, "Read audio packet");
                if (packet->stream_index == stream)
                {
                    if (first_packet_timestamp == AV_NOPTS_VALUE)
                    {
                        first_packet_timestamp = packet->pts;
                    }
                    if (uses_matroska_blocks)
                    {
                        av_buffer_unref(&packet->opaque_ref);
                        packet->opaque_ref = av_buffer_alloc(sizeof(packet->pos));
                        require(packet->opaque_ref != nullptr, "Audio packet timing allocation failed");
                        std::memcpy(packet->opaque_ref->data, &packet->pos, sizeof(packet->pos));
                    }
                    packet_pending = true;
                    break;
                }
                av_packet_unref(packet);
            }
        }
        return false;
    }
    bool convert_next()
    {
        if (resampler_end) { return false; }
        if (gap_input_samples > 0)
        {
            const auto silence = static_cast<int>(std::min<int64_t>(gap_input_samples, 4096));
            check(swr_inject_silence(resampler, silence), "Restore missing audio samples");
            gap_input_samples -= silence;
            next_input_sample += silence;
            const auto capacity = swr_get_out_samples(resampler, 0);
            require(capacity >= 0 && capacity <= MAX_CONVERTED_FRAMES, "Audio gap block exceeds capacity");
            pending.resize(static_cast<size_t>(capacity) * channels);
            auto *destination = reinterpret_cast<uint8_t *>(pending.data());
            const auto count = swr_convert(resampler, &destination, capacity,
                const_cast<const uint8_t **>(frame->extended_data), 0);
            check(count, "Resample missing audio");
            pending.resize(static_cast<size_t>(count) * channels);
            pending_start = next_sample;
            next_sample += count;
            pending_offset = 0;
            if (std::all_of(pending.begin(), pending.end(), [](float value) { return value == 0; }))
            {
                pending.clear();
            }
            return true;
        }
        const auto has_frame = frame_held || receive();
        frame_held = false;
        if (cancelled.load()) { return false; }
        int output_capacity = 4096;
        int input_samples = 0;
        int input_offset = 0;
        if (has_frame)
        {
            require(frame->sample_rate == codec->sample_rate && frame->format == codec->sample_fmt &&
                av_channel_layout_compare(&frame->ch_layout, &codec->ch_layout) == 0,
                "Midstream audio format changes are unsupported");
            int64_t block_position = -1;
            if (uses_matroska_blocks && frame->opaque_ref && frame->opaque_ref->size == sizeof(block_position))
            {
                std::memcpy(&block_position, frame->opaque_ref->data, sizeof(block_position));
            }
            // Laces share a physical block; only its first frame anchors the PCM clock.
            const auto block_continuation = block_position >= 0 && block_position == last_block_position;
            last_block_position = block_position;
            const auto timestamp = frame->best_effort_timestamp;
            if (timestamp != AV_NOPTS_VALUE)
            {
                const auto input_sample = av_rescale_q(timestamp, format->streams[stream]->time_base,
                    AVRational{1, codec->sample_rate});
                auto sample = av_rescale_q(timestamp, format->streams[stream]->time_base, AVRational{1, rate});
                const auto first_input = resample_origin_input == AV_NOPTS_VALUE;
                if (resample_origin_input == AV_NOPTS_VALUE)
                {
                    resample_origin_input = input_sample;
                    resample_origin_output = sample;
                }
                if (align_seek_input)
                {
                    const auto period = codec->sample_rate / std::gcd(codec->sample_rate, rate);
                    const auto phase = ((input_sample - resample_origin_input) % period + period) % period;
                    input_offset = static_cast<int>((period - phase) % period);
                    sample = resample_origin_output + av_rescale_q(input_sample + input_offset - resample_origin_input,
                        AVRational{1, codec->sample_rate}, AVRational{1, rate});
                    align_seek_input = false;
                }
                const auto delay = swr_get_delay(resampler, rate);
                const auto timestamp_resolution = av_rescale_q_rnd(1, format->streams[stream]->time_base,
                    AVRational{1, rate}, AV_ROUND_UP);
                if (next_sample == AV_NOPTS_VALUE)
                {
                    next_sample = sample;
                    next_input_sample = input_sample + input_offset;
                    const auto origin = format->start_time == AV_NOPTS_VALUE ? 0 : format->start_time;
                    const auto origin_input = av_rescale_q(origin, AV_TIME_BASE_Q, AVRational{1, codec->sample_rate});
                    if (asynchronous_timestamps && first_input && input_sample > origin_input)
                    {
                        next_sample = av_rescale_q(origin, AV_TIME_BASE_Q, AVRational{1, rate});
                        next_input_sample = origin_input;
                        gap_input_samples = input_sample - origin_input;
                        frame_held = true;
                        pending.clear();
                        pending_offset = 0;
                        return true;
                    }
                }
                else if (!block_continuation &&
                    std::llabs(sample - (next_sample + delay)) > std::max<int64_t>(
                        asynchronous_timestamps ? static_cast<int64_t>(rate) * TIMESTAMP_HARD_COMPENSATION_MILLISECONDS / 1000 : 2,
                        timestamp_resolution + 1))
                {
                    if (asynchronous_timestamps && input_sample > next_input_sample)
                    {
                        gap_input_samples = input_sample - next_input_sample;
                        frame_held = true;
                        pending.clear();
                        pending_offset = 0;
                        return true;
                    }
                    swr_close(resampler);
                    check(swr_init(resampler), "Reset discontinuous audio resampler");
                    next_sample = sample;
                    next_input_sample = input_sample;
                }
            }
            require(next_sample != AV_NOPTS_VALUE, "Audio frame has no usable timestamp");
            input_samples = frame->nb_samples;
            const auto time_base = format->streams[stream]->time_base;
            if (frame->duration > 0 && av_cmp_q(time_base, AVRational{1, frame->sample_rate}) <= 0)
            {
                const auto duration_samples = av_rescale_q_rnd(frame->duration, time_base,
                    AVRational{1, frame->sample_rate}, AV_ROUND_DOWN);
                input_samples = static_cast<int>(std::min<int64_t>(input_samples, duration_samples));
            }
            if (input_offset >= input_samples && input_offset > 0)
            {
                align_seek_input = true;
                pending.clear();
                pending_offset = 0;
                return true;
            }
            input_samples -= input_offset;
            const auto planar = av_sample_fmt_is_planar(codec->sample_fmt);
            const auto plane_count = planar ? codec->ch_layout.nb_channels : 1;
            const auto sample_bytes = av_get_bytes_per_sample(codec->sample_fmt) * (planar ? 1 : codec->ch_layout.nb_channels);
            input_planes.resize(plane_count);
            for (auto plane = 0; plane < plane_count; ++plane)
            {
                input_planes[plane] = frame->extended_data[plane] + input_offset * sample_bytes;
            }
            output_capacity = swr_get_out_samples(resampler, input_samples);
        }
        else if (next_sample == AV_NOPTS_VALUE) { resampler_end = true; return false; }
        require(output_capacity >= 0 && output_capacity <= MAX_CONVERTED_FRAMES, "Decoded audio block exceeds capacity");
        pending.resize(static_cast<size_t>(output_capacity) * channels);
        auto *destination = reinterpret_cast<uint8_t *>(pending.data());
        const auto count = swr_convert(resampler, &destination, output_capacity,
            has_frame ? input_planes.data() : nullptr, input_samples);
        check(count, "Resample audio");
        next_input_sample += input_samples;
        pending.resize(static_cast<size_t>(count) * channels);
        pending_start = next_sample;
        next_sample += count;
        pending_offset = 0;
        if (!has_frame && count == 0) { resampler_end = true; }
        return !resampler_end;
    }
    int read(float *samples, int frame_capacity, int *frames, int64_t *start)
    {
        require(codec && samples && frames && start && frame_capacity > 0 && frame_capacity <= MAX_CONVERTED_FRAMES,
            "Invalid audio read arguments");
        *frames = 0;
        while (!cancelled.load())
        {
            const auto total = static_cast<int>(pending.size()) / channels;
            if (pending_offset < total)
            {
                if (minimum_sample > pending_start + pending_offset)
                {
                    const auto trim = std::min<int64_t>(total - pending_offset, minimum_sample - pending_start - pending_offset);
                    pending_offset += static_cast<int>(trim);
                }
                const auto count = std::min(frame_capacity, total - pending_offset);
                if (count)
                {
                    std::memcpy(samples, pending.data() + pending_offset * channels, static_cast<size_t>(count) * channels * sizeof(float));
                    *start = pending_start + pending_offset;
                    *frames = count;
                    pending_offset += count;
                    return 0;
                }
            }
            if (!convert_next()) { return cancelled.load() ? 6 : 1; }
        }
        return 6;
    }
    void seek(int64_t sample)
    {
        require(codec != nullptr, "Audio decoder is not open");
        const auto preroll = std::max<int64_t>(static_cast<int64_t>(rate) * SEEK_PREROLL_SECONDS,
            av_rescale_q(codec->seek_preroll, AVRational{1, codec->sample_rate}, AVRational{1, rate}));
        if (resample_origin_input == AV_NOPTS_VALUE && convert_next() && sample <= pending_start + preroll)
        {
            minimum_sample = sample;
            return;
        }
        const auto beginning = sample < std::numeric_limits<int64_t>::min() + preroll ?
            std::numeric_limits<int64_t>::min() : sample - preroll;
        auto target = av_rescale_q(beginning, AVRational{1, rate}, format->streams[stream]->time_base);
        const auto *track = format->streams[stream];
        if (track->start_time != AV_NOPTS_VALUE)
        {
            target = std::max(target, first_packet_timestamp == AV_NOPTS_VALUE ? track->start_time :
                std::min(track->start_time, first_packet_timestamp));
            if (track->duration > 0 && track->duration < std::numeric_limits<int64_t>::max() - std::max<int64_t>(0, track->start_time))
            {
                target = std::min(target, track->start_time + track->duration - 1);
            }
        }
        check(avformat_seek_file(format, stream, std::numeric_limits<int64_t>::min(), target, target, AVSEEK_FLAG_BACKWARD), "Seek audio");
        avcodec_flush_buffers(codec);
        av_packet_unref(packet);
        av_frame_unref(frame);
        swr_close(resampler);
        check(swr_init(resampler), "Reset audio resampler");
        input_end = sent_end = resampler_end = packet_pending = frame_held = false;
        gap_input_samples = 0;
        next_input_sample = AV_NOPTS_VALUE;
        next_sample = AV_NOPTS_VALUE;
        align_seek_input = true;
        last_block_position = -1;
        minimum_sample = sample;
        pending.clear();
        pending_offset = 0;
    }
};
class SdlOutput final : public AudioOutput
{
public:
    SDL_AudioStream *stream = nullptr;
    int rate = 48000;
    int channels = 2;
    bool initialized = false;
    uint64_t epoch = 0;
    int64_t submitted = 0;
    std::mutex gate;
    SdlOutput()
    {
        require(SDL_GetVersion() == SDL_VERSION && SDL_VERSION == SDL_VERSIONNUM(3, 4, 16), "SDL3 build/runtime version must be 3.4.16");
        require(SDL_InitSubSystem(SDL_INIT_AUDIO), SDL_GetError());
        initialized = true;
        SDL_AudioSpec spec{SDL_AUDIO_F32, channels, rate};
        stream = SDL_OpenAudioDeviceStream(SDL_AUDIO_DEVICE_DEFAULT_PLAYBACK, &spec, nullptr, nullptr);
        if (!stream)
        {
            SDL_QuitSubSystem(SDL_INIT_AUDIO);
            initialized = false;
            throw std::runtime_error(SDL_GetError());
        }
    }
    ~SdlOutput() override
    {
        if (stream) { SDL_DestroyAudioStream(stream); }
        if (initialized) { SDL_QuitSubSystem(SDL_INIT_AUDIO); }
    }
    void write(const float *samples, int frames) override
    {
        std::scoped_lock lock(gate);
        const auto queued = SDL_GetAudioStreamQueued(stream);
        require(queued >= 0 && queued + frames * channels * 4 <= 12000 * channels * 4, "Audio playback queue capacity exceeded");
        require(SDL_PutAudioStreamData(stream, samples, frames * channels * 4), SDL_GetError());
        submitted += frames;
    }
    void pause(bool paused) override
    {
        require(paused ? SDL_PauseAudioStreamDevice(stream) : SDL_ResumeAudioStreamDevice(stream), SDL_GetError());
    }
    void clear() override
    {
        std::scoped_lock lock(gate);
        require(SDL_ClearAudioStream(stream), SDL_GetError());
        submitted = 0;
        ++epoch;
    }
    void gain(float value) override
    {
        require(SDL_SetAudioStreamGain(stream, value), SDL_GetError());
    }
    float gain() override
    {
        const auto value = SDL_GetAudioStreamGain(stream);
        require(value >= 0, SDL_GetError());
        return value;
    }
    int latency() const override
    {
        SDL_AudioSpec spec{};
        int frames = 0;
        require(SDL_GetAudioDeviceFormat(SDL_GetAudioStreamDevice(stream), &spec, &frames) && spec.freq > 0, SDL_GetError());
        return static_cast<int>(av_rescale_rnd(frames, rate, spec.freq, AV_ROUND_UP));
    }
    an_audio_clock_snapshot snapshot() override
    {
        std::scoped_lock lock(gate);
        const auto bytes = SDL_GetAudioStreamQueued(stream);
        require(bytes >= 0, SDL_GetError());
        an_audio_clock_snapshot result{};
        result.size = sizeof(result);
        result.quality = 1;
        result.queued_frames = bytes / (channels * 4);
        result.played_frames = std::max<int64_t>(0, submitted - result.queued_frames - latency());
        result.host_timestamp = SDL_GetTicksNS();
        result.host_frequency = 1000000000;
        result.epoch = epoch;
        result.sample_rate = rate;
        result.channels = channels;
        result.backend = 3;
        std::snprintf(result.device_id, sizeof(result.device_id), "sdl-default-estimated");
        return result;
    }
};
}

uint32_t an_audio_abi_version() { return 2; }
uint32_t an_audio_clock_snapshot_size() { return sizeof(an_audio_clock_snapshot); }
int an_audio_decoder_create(void **decoder, char *error, uint32_t capacity)
{
    return boundary(error, capacity, [&] { require(decoder, "Missing decoder output"); *decoder = new Decoder(); return 0; });
}
int an_audio_decoder_set_timestamp_policy(void *decoder, int policy, char *error, uint32_t capacity)
{
    return boundary(error, capacity, [&]
    {
        require(decoder && (policy == 0 || policy == 1), "Invalid audio timestamp policy");
        auto *value = static_cast<Decoder *>(decoder);
        require(!value->format, "Audio timestamp policy must be selected before opening the source");
        value->asynchronous_timestamps = policy == 1;
        return 0;
    });
}
int an_audio_decoder_open(void *decoder, const char *path, int stream, int rate, int channels, char *error, uint32_t capacity)
{
    const auto result = boundary(error, capacity, [&] { require(decoder, "Missing decoder"); static_cast<Decoder *>(decoder)->open(path, stream, rate, channels); return 0; });
    return decoder && static_cast<Decoder *>(decoder)->cancelled.load() ? 6 : result;
}
int an_audio_decoder_read(void *decoder, float *samples, int frame_capacity, int *frames, int64_t *start, char *error, uint32_t capacity)
{
    const auto result = boundary(error, capacity, [&] { require(decoder, "Missing decoder"); return static_cast<Decoder *>(decoder)->read(samples, frame_capacity, frames, start); });
    return decoder && static_cast<Decoder *>(decoder)->cancelled.load() ? 6 : result;
}
int an_audio_decoder_seek(void *decoder, int64_t sample, char *error, uint32_t capacity)
{
    const auto result = boundary(error, capacity, [&] { require(decoder, "Missing decoder"); static_cast<Decoder *>(decoder)->seek(sample); return 0; });
    return decoder && static_cast<Decoder *>(decoder)->cancelled.load() ? 6 : result;
}
void an_audio_decoder_cancel(void *decoder) { if (decoder) { static_cast<Decoder *>(decoder)->cancelled.store(true); } }
void an_audio_decoder_destroy(void *decoder) { delete static_cast<Decoder *>(decoder); }
int an_audio_output_create(void **output, int rate, int channels, char *error, uint32_t capacity)
{
    return boundary(error, capacity, [&]
    {
        require(output && rate == 48000 && channels == 2, "Playback requires 48kHz stereo");
        *output = nullptr;
        *output = new SdlOutput();
        return 0;
    });
}
int an_audio_output_create_system(void **output, int rate, int channels, char *error, uint32_t capacity)
{
    return boundary(error, capacity, [&]
    {
        require(output && rate == 48000 && channels == 2, "Playback requires 48kHz stereo");
        *output = nullptr;
#if defined(__APPLE__) || defined(_WIN32)
        *output = create_system_audio_output().release();
#else
        *output = new SdlOutput();
#endif
        return 0;
    });
}
int an_audio_output_snapshot(void *output, an_audio_clock_snapshot *snapshot, char *error, uint32_t capacity)
{
    return boundary(error, capacity, [&]
    {
        require(output && snapshot && snapshot->size == sizeof(an_audio_clock_snapshot), "Audio clock snapshot ABI mismatch");
        *snapshot = static_cast<AudioOutput *>(output)->snapshot();
        return 0;
    });
}
int an_audio_output_write(void *output, const float *samples, int frames, char *error, uint32_t capacity)
{
    return boundary(error, capacity, [&]
    {
        require(output && samples && frames > 0 && frames <= 12000, "Invalid playback PCM block");
        static_cast<AudioOutput *>(output)->write(samples, frames);
        return 0;
    });
}
int an_audio_output_pause(void *output, int pause, char *error, uint32_t capacity)
{
    return boundary(error, capacity, [&] { require(output, "Missing audio output");
        static_cast<AudioOutput *>(output)->pause(pause != 0); return 0; });
}
int an_audio_output_clear(void *output, char *error, uint32_t capacity)
{
    return boundary(error, capacity, [&] { require(output, "Missing audio output"); static_cast<AudioOutput *>(output)->clear(); return 0; });
}
int an_audio_output_queued(void *output)
{
    if (!output) { return -1; }
    try { return static_cast<AudioOutput *>(output)->snapshot().queued_frames; }
    catch (...) { return -1; }
}
int an_audio_output_latency(void *output)
{
    if (!output) { return -1; }
    try { return static_cast<AudioOutput *>(output)->latency(); }
    catch (...) { return -1; }
}
int an_audio_output_gain(void *output, float gain, char *error, uint32_t capacity)
{
    return boundary(error, capacity, [&] { require(output && std::isfinite(gain) && gain >= 0 && gain <= 1, "Invalid audio gain");
        static_cast<AudioOutput *>(output)->gain(gain); return 0; });
}
void an_audio_output_destroy(void *output) { delete static_cast<AudioOutput *>(output); }
