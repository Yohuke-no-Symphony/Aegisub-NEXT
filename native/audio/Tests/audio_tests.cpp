#include "aeginext_audio.h"
#include "audio_output_timeline.h"
#include "audio_output.h"
#include "core_audio_clock_continuity.h"
#include <SDL3/SDL.h>
#include <algorithm>
#include <array>
#include <cmath>
#include <cstdlib>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <stdexcept>
#include <vector>

static void require(bool condition, const char *message)
{
    if (!condition) { throw std::runtime_error(message); }
}
static void put(std::ofstream &file, uint32_t value, int bytes)
{
    for (int index = 0; index < bytes; ++index) { file.put(static_cast<char>((value >> (index * 8)) & 255)); }
}
static void require_gain(void *output, float expected, const char *message)
{
    const auto actual = static_cast<AudioOutput *>(output)->gain();
    require(std::abs(actual - expected) < 0.000001F, message);
}
static void verify_seek_phase(int input_rate)
{
    const auto path = std::filesystem::temp_directory_path() / ("aeginext-audio-seek-" + std::to_string(input_rate) + ".wav");
    void *decoder = nullptr;
    char error[1024]{};
    try
    {
        {
            const auto count = input_rate * 5;
            std::ofstream file(path, std::ios::binary);
            file.write("RIFF", 4); put(file, 36 + count * 2, 4); file.write("WAVEfmt ", 8);
            put(file, 16, 4); put(file, 1, 2); put(file, 1, 2); put(file, input_rate, 4);
            put(file, input_rate * 2, 4); put(file, 2, 2); put(file, 16, 2); file.write("data", 4); put(file, count * 2, 4);
            uint32_t state = 123456789;
            for (auto index = 0; index < count; ++index)
            {
                state = state * 1664525U + 1013904223U;
                const auto sample = static_cast<int16_t>((state >> 17) - 16384);
                put(file, static_cast<uint16_t>(sample), 2);
            }
        }
        require(an_audio_decoder_create(&decoder, error, sizeof(error)) == 0, error);
        require(an_audio_decoder_open(decoder, path.string().c_str(), 0, 16000, 1, error, sizeof(error)) == 0, error);
        std::vector<float> samples(4096);
        std::vector<float> reference;
        while (true)
        {
            int count = 0;
            int64_t start = 0;
            const auto status = an_audio_decoder_read(decoder, samples.data(), 4096, &count, &start, error, sizeof(error));
            if (status == 1) { break; }
            require(status == 0, error);
            require(start == static_cast<int64_t>(reference.size()), "Sequential PCM has a stable global sample grid");
            reference.insert(reference.end(), samples.begin(), samples.begin() + count);
        }
        require(reference.size() == 80000, "Reference PCM preserves the full resampled duration");
        for (const auto reopen : {false, true})
        {
            for (const auto requested : {1, 16591, 40001, 79901, 0, 31999})
            {
                if (reopen)
                {
                    an_audio_decoder_destroy(decoder);
                    decoder = nullptr;
                    require(an_audio_decoder_create(&decoder, error, sizeof(error)) == 0, error);
                    require(an_audio_decoder_open(decoder, path.string().c_str(), 0, 16000, 1, error, sizeof(error)) == 0, error);
                }
                require(an_audio_decoder_seek(decoder, requested, error, sizeof(error)) == 0, error);
                auto expected = requested;
                const auto end = std::min(80000, requested + 3200);
                while (expected < end)
                {
                    int count = 0;
                    int64_t start = 0;
                    const auto status = an_audio_decoder_read(decoder, samples.data(), std::min(4096, end - expected), &count, &start, error, sizeof(error));
                    require(status == 0, error);
                    require(start == expected && count > 0, "Seek returns the exact global PCM sample and does not shorten the tail");
                    for (auto index = 0; index < count; ++index)
                    {
                        require(std::abs(samples[index] - reference[expected + index]) <= 0.000001F,
                            "Seek PCM must retain the same resampler phase as continuous decoding");
                    }
                    expected += count;
                }
            }
        }
        an_audio_decoder_destroy(decoder);
        std::filesystem::remove(path);
    }
    catch (...)
    {
        an_audio_decoder_destroy(decoder);
        std::filesystem::remove(path);
        throw;
    }
}
int main()
{
    void *decoder = nullptr;
    void *output = nullptr;
    const auto path = std::filesystem::temp_directory_path() / "aeginext-audio-contract.wav";
    try
    {
        {
            std::ofstream file(path, std::ios::binary);
            file.write("RIFF", 4); put(file, 36 + 44100 * 2, 4); file.write("WAVEfmt ", 8);
            put(file, 16, 4); put(file, 1, 2); put(file, 1, 2); put(file, 44100, 4);
            put(file, 88200, 4); put(file, 2, 2); put(file, 16, 2); file.write("data", 4); put(file, 88200, 4);
            for (int index = 0; index < 44100; ++index)
            {
                const auto sample = static_cast<int16_t>(12000 * std::sin(index * 6.283185307179586 * 440 / 44100));
                put(file, static_cast<uint16_t>(sample), 2);
            }
        }
        char error[1024]{};
        require(an_audio_abi_version() == 2 && an_audio_clock_snapshot_size() == sizeof(an_audio_clock_snapshot), "ABI and clock layout");
        AudioOutputTimeline timeline;
        timeline.append(100, 0, 100, 200);
        require(timeline.read(99) == 0 && timeline.read(150) == 50 && timeline.read(250) == 100, "Future frames and underflow silence do not move media position");
        timeline.append(300, 100, 60, 100);
        require(timeline.read(330) == 130 && timeline.read(400) == 160, "Device position maps to real PCM after underflow");
        timeline.reset();
        require(timeline.read(10000) == 0, "Seek clears device-to-media mapping");
        CoreAudioClockContinuity queue_clock;
        require(queue_clock.can_wait_for_timestamp(true, false), "A newly started queue may not yet have a hardware timestamp");
        require(queue_clock.observe(-1, false, true, true) == CoreAudioClockObservation::WAITING, "A new queue waits for a future start timestamp without observing negative PCM");
        require(queue_clock.observe(0, false, true, true) == CoreAudioClockObservation::WAITING, "A new queue waits until its hardware sample clock advances");
        require(queue_clock.observe(-1, false, true, false) == CoreAudioClockObservation::INVALID, "A negative initial timestamp cannot outlive the startup grace period");
        require(queue_clock.observe(0, false, true, false) == CoreAudioClockObservation::INVALID, "An initial clock that never advances must fail after the startup grace period");
        require(queue_clock.observe(100, false, true, true) == CoreAudioClockObservation::ACCEPTED, "Initial observed CoreAudio sample frame");
        require(queue_clock.observe(150, true, true, false) == CoreAudioClockObservation::ACCEPTED, "An overload discontinuity with an unchanged route and monotonic sample time must retain the system clock");
        require(!queue_clock.can_wait_for_timestamp(true, false), "A running observed clock cannot hide a missing timestamp");
        require(queue_clock.can_wait_for_timestamp(true, true), "A paused queue may lack a running timestamp without changing the device");
        queue_clock.begin_run();
        require(queue_clock.can_wait_for_timestamp(true, false), "Resume waits for the queue to restart without resetting its media sample mapping");
        require(queue_clock.observe(149, false, true, true) == CoreAudioClockObservation::WAITING, "A resumed queue must wait for its initial stale timestamp instead of invalidating the device");
        require(queue_clock.observe(150, false, true, true) == CoreAudioClockObservation::WAITING, "An unchanged resume timestamp must not prematurely end the startup grace period");
        require(queue_clock.can_wait_for_timestamp(true, false), "Stale resume observations retain startup ownership");
        require(queue_clock.observe(149, false, true, false) == CoreAudioClockObservation::INVALID, "A stale resume timestamp must fail after the startup grace period");
        require(queue_clock.observe(150, false, true, false) == CoreAudioClockObservation::INVALID, "An unchanged resume timestamp must fail after the startup grace period");
        require(queue_clock.observe(151, false, true, true) == CoreAudioClockObservation::ACCEPTED, "An advancing resume timestamp retains the previous sample origin");
        require(!queue_clock.can_wait_for_timestamp(true, false), "An advancing resume timestamp ends startup ownership");
        require(queue_clock.observe(151, false, true, true) == CoreAudioClockObservation::ACCEPTED, "Repeated steady timestamps remain valid for high-frequency reads");
        require(queue_clock.observe(150, true, true, true) == CoreAudioClockObservation::INVALID, "A steady clock regression must fail even within the original startup grace period");
        queue_clock.begin_run();
        require(queue_clock.observe(150, true, false, true) == CoreAudioClockObservation::INVALID, "A real route change must fail immediately while waiting for resume");
        require(queue_clock.observe(160, true, false, true) == CoreAudioClockObservation::INVALID, "A real route change must fail closed despite a monotonic timestamp");
        require(queue_clock.observe(160, false, false, true) == CoreAudioClockObservation::INVALID, "A real route change must fail closed without a discontinuity flag");
        queue_clock.reset();
        require(queue_clock.observe(0, false, true, true) == CoreAudioClockObservation::WAITING && timeline.read(0) == 0, "Clear begins a new queue sample timeline without reusing old PCM");
        timeline.append(0, 0, 100, 200);
        require(queue_clock.observe(150, true, true, true) == CoreAudioClockObservation::ACCEPTED && timeline.read(150) == 100, "Overload and underflow silence do not invent media PCM");
        timeline.append(300, 100, 60, 100);
        require(queue_clock.observe(330, true, true, false) == CoreAudioClockObservation::ACCEPTED && timeline.read(330) == 130, "Real PCM after a discontinuity retains exact queue-to-media mapping");
        require(an_audio_decoder_create(&decoder, error, sizeof(error)) == 0, error);
        require(an_audio_decoder_open(decoder, path.string().c_str(), 0, 16000, 1, error, sizeof(error)) == 0, error);
        std::vector<float> samples(4096);
        int total = 0;
        double energy = 0;
        int64_t expected = 0;
        while (true)
        {
            int frames = 0;
            int64_t start = 0;
            const auto status = an_audio_decoder_read(decoder, samples.data(), 4096, &frames, &start, error, sizeof(error));
            if (status == 1) { break; }
            require(status == 0, error);
            require(start == expected && frames > 0 && frames <= 4096, "PCM timestamp and capacity");
            expected += frames;
            total += frames;
            for (int index = 0; index < frames; ++index) { energy += samples[index] * samples[index]; }
        }
        require(total == 16000 && energy / total > 0.01, "Resampled duration and audible energy");
        require(an_audio_decoder_seek(decoder, 4000, error, sizeof(error)) == 0, error);
        int frames = 0;
        int64_t start = 0;
        require(an_audio_decoder_read(decoder, samples.data(), 4096, &frames, &start, error, sizeof(error)) == 0, error);
        require(start == 4000 && frames > 0, "Sample accurate seek trim");
        an_audio_decoder_cancel(decoder);
        require(an_audio_decoder_read(decoder, samples.data(), 4096, &frames, &start, error, sizeof(error)) == 6, "Permanent cancellation");
        an_audio_decoder_destroy(decoder); decoder = nullptr;
        std::cout << "PASS resampling, PTS, seek, cancellation\n";
        verify_seek_phase(44100);
        verify_seek_phase(48000);
        std::cout << "PASS 44.1/48 kHz continuous and random-seek PCM phase parity\n";

        require(an_audio_output_create(&output, 48000, 2, error, sizeof(error)) == 0, error);
        std::vector<float> stereo(24000, 0.1F);
        require(an_audio_output_write(output, stereo.data(), 12000, error, sizeof(error)) == 0, error);
        require(an_audio_output_queued(output) == 12000, "Paused queue retains PCM");
        require(an_audio_output_write(output, stereo.data(), 1, error, sizeof(error)) != 0, "Output queue bounded");
        require(an_audio_output_latency(output) > 0, "Device latency estimate");
        an_audio_clock_snapshot clock{};
        clock.size = sizeof(clock);
        require(an_audio_output_snapshot(output, &clock, error, sizeof(error)) == 0 && clock.quality == 1 && clock.backend == 3, "SDL clock is explicitly estimated");
        auto wrong_size = clock;
        wrong_size.size = 1;
        require(an_audio_output_snapshot(output, &wrong_size, error, sizeof(error)) != 0, "Clock ABI size is checked");
        require(an_audio_output_gain(output, 0.5F, error, sizeof(error)) == 0, error);
        require_gain(output, 0.5F, "SDL stream applies the requested gain");
        require(an_audio_output_pause(output, 0, error, sizeof(error)) == 0, error);
        SDL_Delay(100);
        require(an_audio_output_queued(output) < 12000, "Device consumes PCM");
        require(an_audio_output_pause(output, 1, error, sizeof(error)) == 0, error);
        const auto paused = an_audio_output_queued(output);
        SDL_Delay(50);
        require(an_audio_output_queued(output) == paused, "Paused device freezes consumption");
        require(an_audio_output_clear(output, error, sizeof(error)) == 0 && an_audio_output_queued(output) == 0, "Seek clears device queue");
        require_gain(output, 0.5F, "SDL clear retains the stream gain");
        an_audio_output_destroy(output); output = nullptr;
        if (std::getenv("AEGINEXT_RUN_SYSTEM_AUDIO_TESTS"))
        {
            require(an_audio_output_create_system(&output, 48000, 2, error, sizeof(error)) == 0, error);
            std::fill(stereo.begin(), stereo.end(), 0.0F);
            for (const auto expected_gain : {0.0F, 0.35F})
            {
                require(an_audio_output_gain(output, expected_gain, error, sizeof(error)) == 0, error);
                require_gain(output, expected_gain, "System output applies mute and ordinary volume to the real queue");
                require(an_audio_output_clear(output, error, sizeof(error)) == 0, error);
                require_gain(output, expected_gain, "System seek preserves mute and ordinary volume on the real queue");
                require(an_audio_output_write(output, stereo.data(), 1200, error, sizeof(error)) == 0, error);
                require(an_audio_output_pause(output, 0, error, sizeof(error)) == 0, error);
                require_gain(output, expected_gain, "System resume preserves mute and ordinary volume on the real queue");
                SDL_Delay(20);
                require(an_audio_output_pause(output, 1, error, sizeof(error)) == 0, error);
            }
            std::cout << "PASS system mute and ordinary volume survive seek and resume (real backend gain)\n";
            require(an_audio_output_pause(output, 1, error, sizeof(error)) == 0, error);
            require(an_audio_output_clear(output, error, sizeof(error)) == 0, error);
            std::fill(stereo.begin(), stereo.end(), 0.0F);
            require(an_audio_output_snapshot(output, &clock, error, sizeof(error)) == 0 && clock.quality == 2 && clock.device_id[0] != 0, "System clock and device identity");
            require(an_audio_output_write(output, stereo.data(), 12000, error, sizeof(error)) == 0, error);
            require(an_audio_output_pause(output, 0, error, sizeof(error)) == 0, error);
            SDL_Delay(100);
            require(an_audio_output_snapshot(output, &clock, error, sizeof(error)) == 0 && clock.quality == 2 && clock.played_frames > 0 && clock.played_frames < 12000, "System device clock advances within real PCM");
            require(an_audio_output_pause(output, 1, error, sizeof(error)) == 0, error);
            require(an_audio_output_snapshot(output, &clock, error, sizeof(error)) == 0, error);
            const auto frozen = clock.played_frames;
            SDL_Delay(50);
            require(an_audio_output_snapshot(output, &clock, error, sizeof(error)) == 0 && clock.played_frames == frozen, "System pause freezes output clock");
            const auto epoch = clock.epoch;
            require(an_audio_output_clear(output, error, sizeof(error)) == 0, error);
            require(an_audio_output_snapshot(output, &clock, error, sizeof(error)) == 0 && clock.played_frames == 0 && clock.epoch > epoch && clock.queued_frames == 0, "System seek resets clock epoch");
            require(an_audio_output_write(output, stereo.data(), 1200, error, sizeof(error)) == 0, error);
            require(an_audio_output_pause(output, 0, error, sizeof(error)) == 0, error);
            SDL_Delay(150);
            require(an_audio_output_snapshot(output, &clock, error, sizeof(error)) == 0 && clock.quality == 2 && clock.played_frames == 1200, "Device underflow silence freezes media position");
            require(an_audio_output_write(output, stereo.data(), 4800, error, sizeof(error)) == 0, error);
            SDL_Delay(180);
            require(an_audio_output_snapshot(output, &clock, error, sizeof(error)) == 0 && clock.quality == 2 && clock.played_frames == 6000, "PCM resumes after underflow without absorbing the silent device gap");
            require(an_audio_output_pause(output, 1, error, sizeof(error)) == 0, error);
            uint64_t previous_epoch = clock.epoch;
            int64_t previous_played = clock.played_frames;
            for (int index = 0; index < 500; ++index)
            {
                if (index % 50 == 0)
                {
                    require(an_audio_output_pause(output, 1, error, sizeof(error)) == 0, error);
                    require(an_audio_output_clear(output, error, sizeof(error)) == 0, error);
                    require_gain(output, 0.35F, "Repeated queue recreation retains ordinary volume");
                    require(an_audio_output_snapshot(output, &clock, error, sizeof(error)) == 0 && clock.quality == 2 &&
                        clock.played_frames == 0 && clock.epoch > previous_epoch, "Repeated seek retains the real system clock in a new queue epoch");
                    previous_epoch = clock.epoch;
                    previous_played = 0;
                    require(an_audio_output_write(output, stereo.data(), 1200, error, sizeof(error)) == 0, error);
                    require(an_audio_output_pause(output, 0, error, sizeof(error)) == 0, error);
                }
                require(an_audio_output_snapshot(output, &clock, error, sizeof(error)) == 0 && clock.quality == 2, "Repeated queue restart does not confuse discontinuity with a device change");
                require(clock.played_frames >= previous_played, "Observed hardware PCM position stays monotonic within each queue epoch");
                previous_played = clock.played_frames;
                if (index % 50 >= 5)
                {
                    const auto refill = std::min(4096, 9600 - clock.queued_frames);
                    if (refill > 0)
                    {
                        require(an_audio_output_write(output, stereo.data(), refill, error, sizeof(error)) == 0, error);
                    }
                }
                SDL_Delay(20);
            }
            require(an_audio_output_pause(output, 1, error, sizeof(error)) == 0, error);
#if defined(__APPLE__)
            require(an_audio_output_clear(output, error, sizeof(error)) == 0, error);
            require(an_audio_output_write(output, stereo.data(), 9600, error, sizeof(error)) == 0, error);
            require(an_audio_output_pause(output, 0, error, sizeof(error)) == 0, error);
            SDL_Delay(100);
            require(an_audio_output_snapshot(output, &clock, error, sizeof(error)) == 0 && clock.quality == 2 && clock.played_frames > 0,
                "Rapid resume regression starts from an observed hardware clock");
            const auto resume_epoch = clock.epoch;
            const std::array<Uint32, 8> resume_delays{0, 1, 2, 4, 8, 16, 40, 80};
            for (size_t index = 0; index < 64; ++index)
            {
                require(an_audio_output_pause(output, 1, error, sizeof(error)) == 0, error);
                require(an_audio_output_snapshot(output, &clock, error, sizeof(error)) == 0 && clock.quality == 2 && clock.epoch == resume_epoch,
                    "Rapid pause retains the observed system clock and queue epoch");
                const auto paused_played = clock.played_frames;
                const auto refill = std::min(4096, 9600 - clock.queued_frames);
                if (refill > 0)
                {
                    require(an_audio_output_write(output, stereo.data(), refill, error, sizeof(error)) == 0, error);
                }
                require(an_audio_output_pause(output, 0, error, sizeof(error)) == 0, error);
                require(an_audio_output_snapshot(output, &clock, error, sizeof(error)) == 0 && clock.quality == 2 &&
                    clock.epoch == resume_epoch && clock.played_frames >= paused_played,
                    "Immediate resume reads retain the last consumed PCM while the hardware timestamp settles");
                SDL_Delay(resume_delays[index % resume_delays.size()]);
                require(an_audio_output_snapshot(output, &clock, error, sizeof(error)) == 0 && clock.quality == 2 &&
                    clock.epoch == resume_epoch && clock.played_frames >= paused_played,
                    "Rapid pause and resume preserve a monotonic media clock within the same queue epoch");
            }
            const auto last_resumed_position = clock.played_frames;
            const auto refill = std::min(4096, 9600 - clock.queued_frames);
            if (refill > 0)
            {
                require(an_audio_output_write(output, stereo.data(), refill, error, sizeof(error)) == 0, error);
            }
            SDL_Delay(100);
            require(an_audio_output_snapshot(output, &clock, error, sizeof(error)) == 0 && clock.quality == 2 &&
                clock.epoch == resume_epoch && clock.played_frames > last_resumed_position,
                "The system clock advances again after rapid resume waiting");
            for (int index = 0; index < 32; ++index)
            {
                require(an_audio_output_pause(output, 1, error, sizeof(error)) == 0, error);
                const auto previous_queue_epoch = clock.epoch;
                require(an_audio_output_clear(output, error, sizeof(error)) == 0, error);
                require(an_audio_output_snapshot(output, &clock, error, sizeof(error)) == 0 && clock.quality == 2 &&
                    clock.epoch > previous_queue_epoch && clock.played_frames == 0, "Rapid seeking rebinds a new queue epoch");
                require(an_audio_output_write(output, stereo.data(), 9600, error, sizeof(error)) == 0, error);
                require(an_audio_output_pause(output, 0, error, sizeof(error)) == 0, error);
                require(an_audio_output_pause(output, 1, error, sizeof(error)) == 0, error);
                require(an_audio_output_pause(output, 0, error, sizeof(error)) == 0, error);
                require(an_audio_output_snapshot(output, &clock, error, sizeof(error)) == 0 && clock.quality == 2,
                    "An immediately paused and resumed seek queue retains its system clock");
                SDL_Delay(40);
                require(an_audio_output_snapshot(output, &clock, error, sizeof(error)) == 0 && clock.quality == 2 && clock.played_frames > 0,
                    "An immediately restarted seek queue resumes consuming real PCM");
            }
            require(an_audio_output_pause(output, 1, error, sizeof(error)) == 0, error);
            std::cout << "PASS CoreAudio rapid resume and immediately restarted seek queues (silent PCM)\n";
#endif
            an_audio_output_destroy(output); output = nullptr;
            std::cout << "PASS system output clock, pause, seek, ten-second restart and underflow stress (silent PCM)\n";
            for (int index = 0; index < 8; ++index)
            {
                require(an_audio_output_create_system(&output, 48000, 2, error, sizeof(error)) == 0, error);
                require(an_audio_output_write(output, stereo.data(), 12000, error, sizeof(error)) == 0, error);
                require(an_audio_output_pause(output, 0, error, sizeof(error)) == 0, error);
                SDL_Delay(80 + index * 5);
                require(an_audio_output_snapshot(output, &clock, error, sizeof(error)) == 0 && clock.quality == 2 &&
                    clock.played_frames > 0 && clock.queued_frames > 0, "Direct close begins while real PCM is playing and queued");
                an_audio_output_destroy(output); output = nullptr;
            }
            std::cout << "PASS direct system output destruction while PCM is playing (silent PCM)\n";
        }
        std::filesystem::remove(path);
        std::cout << "PASS bounded SDL output, pause, clear, gain, resource release\n";
        return 0;
    }
    catch (const std::exception &error)
    {
        an_audio_output_destroy(output);
        an_audio_decoder_destroy(decoder);
        std::filesystem::remove(path);
        std::cerr << error.what() << '\n';
        return 1;
    }
}
