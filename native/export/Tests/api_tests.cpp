#include "aeginext_export.h"
#include <array>
#include <cstddef>
#include <cstring>
#include <iostream>
#include <stdexcept>
void Require(bool value, const char *message)
{
    if (!value) throw std::runtime_error(message);
}
int32_t AN_EXPORT_CALL Render(void *, int64_t, int32_t, int32_t, uint32_t, uint32_t, float *, uint64_t,
    an_export_overlay_info *info)
{
    info->state = AN_EXPORT_OVERLAY_EMPTY;
    info->revision = 1;
    return 0;
}
int main()
{
    try
    {
        static_assert(sizeof(an_export_request) == 88 && offsetof(an_export_request, input_path) == 32 &&
            offsetof(an_export_request, reference_white_nits) == 56 && offsetof(an_export_request, encoding_mode) == 64 &&
            offsetof(an_export_request, rate_control_mode) == 80 && offsetof(an_export_request, rate_control_reserved) == 84);
        static_assert(sizeof(an_export_result_info) == 512 && offsetof(an_export_result_info, fallback_reason) == 72 &&
            offsetof(an_export_result_info, rate_control_mode) == 328 && offsetof(an_export_result_info, video_bitrate) == 332 &&
            offsetof(an_export_result_info, quality) == 336 && offsetof(an_export_result_info, verification) == 340);
        static_assert(sizeof(an_export_overlay_info) == 24 && offsetof(an_export_overlay_info, revision) == 16);
        static_assert(offsetof(an_export_result_info, output_width) == 488 && offsetof(an_export_result_info, output_height) == 492 &&
            offsetof(an_export_result_info, padding_top) == 496 && offsetof(an_export_result_info, padding_bottom) == 500 &&
            offsetof(an_export_result_info, padding_left) == 504 && offsetof(an_export_result_info, padding_right) == 508);
        Require(an_export_abi_version() == AN_EXPORT_ABI_VERSION, "ABI version mismatch");
        Require(an_export_core_version() == 1 && (an_export_capabilities() & 7) == 7,
            "Shared media core version or capabilities are missing");
        std::array<char, 256> error{};
        Require(an_export_create(nullptr, error.data(), error.size()) == 1, "Missing output accepted");
        an_export_destroy(nullptr); an_export_cancel(nullptr);
        for (int i = 0; i < 100; ++i)
        {
            void *context = nullptr;
            Require(an_export_create(&context, error.data(), error.size()) == 0 && context, "Context creation failed");
            uint64_t frames = 123;
            an_export_request request{sizeof(request),AN_EXPORT_ABI_VERSION,0,0,20,64,48,0,"unused","unused","ultrafast",203,0,0,8000000,0,0,1,0};
            an_export_result_info info{};
            info.struct_size = sizeof(info);
            info.abi_version = AN_EXPORT_ABI_VERSION;
            Require(an_export_get_result_info(context, &info, error.data(), error.size()) == 1,
                "Result information reported before completion");
            Require(an_export_get_result_info(context, nullptr, error.data(), error.size()) == 1,
                "Missing result information output accepted");
            info.struct_size = 0;
            Require(an_export_get_result_info(context, &info, error.data(), error.size()) == 1,
                "Invalid result information layout accepted");
            info.struct_size = sizeof(info);
            info.abi_version = 6;
            Require(an_export_get_result_info(context, &info, error.data(), error.size()) == 1,
                "Legacy result information ABI accepted");
            info.abi_version = AN_EXPORT_ABI_VERSION;
            Require(std::strlen(an_export_encoder_name(context)) == 0, "Encoder reported before initialization");
            request.abi_version = 6;
            Require(an_export_run(context, &request, Render, nullptr, &frames, error.data(), error.size()) == 1,
                "Invalid ABI was accepted");
            request.abi_version = AN_EXPORT_ABI_VERSION;
            request.struct_size = 80;
            Require(an_export_run(context, &request, Render, nullptr, &frames, error.data(), error.size()) == 1,
                "Legacy request size accepted");
            request.struct_size = sizeof(request);
            request.reserved = 1;
            Require(an_export_run(context, &request, Render, nullptr, &frames, error.data(), error.size()) == 1,
                "Reserved field was accepted");
            request.reserved = 0;
            request.decode_mode = 3;
            Require(an_export_run(context, &request, Render, nullptr, &frames, error.data(), error.size()) == 1,
                "Unknown decode mode accepted");
            request.decode_mode = 0;
            request.decode_reserved = 1;
            Require(an_export_run(context, &request, Render, nullptr, &frames, error.data(), error.size()) == 1,
                "Decode reserved field was accepted");
            request.decode_reserved = 0;
            request.rate_control_reserved = 1;
            Require(an_export_run(context, &request, Render, nullptr, &frames, error.data(), error.size()) == 1,
                "Rate-control request reserved field accepted");
            request.rate_control_reserved = 0;
            request.rate_control_mode = 0;
            Require(an_export_run(context, &request, Render, nullptr, &frames, error.data(), error.size()) == 1,
                "Unresolved automatic rate-control mode accepted");
            request.rate_control_mode = 4;
            Require(an_export_run(context, &request, Render, nullptr, &frames, error.data(), error.size()) == 1,
                "Unknown rate-control mode accepted");
            request.rate_control_mode = 1;
            request.encoding_mode = 2;
            Require(an_export_run(context, &request, Render, nullptr, &frames, error.data(), error.size()) == 1,
                "Unknown encoding mode accepted");
            request.encoding_mode = 1;
            Require(an_export_run(context, &request, Render, nullptr, &frames, error.data(), error.size()) == 1,
                "Hardware CRF accepted");
            request.rate_control_mode = 2;
            request.video_bitrate = 1;
            Require(an_export_run(context, &request, Render, nullptr, &frames, error.data(), error.size()) == 1,
                "Invalid hardware bitrate accepted");
            request.video_bitrate = 8000000;
            an_export_cancel(context);
            Require(an_export_run(context, &request, Render, nullptr, &frames, error.data(), error.size()) == 4 && frames == 0,
                "Sticky cancellation did not precede opening files");
            Require(an_export_run(context, &request, Render, nullptr, &frames, error.data(), error.size()) == 4,
                "Cancellation was reset");
            for (const auto mode : {1, 2, 3})
            {
                request.rate_control_mode = mode;
                request.encoding_mode = 0;
                request.quality = mode == 1 ? 20 : -100;
                request.video_bitrate = mode == 1 ? -100 : 8000000;
                Require(an_export_run(context, &request, Render, nullptr, &frames, error.data(), error.size()) == 4,
                    "Inactive quality parameter blocked a valid rate-control mode");
                if (mode == 1)
                {
                    for (const auto quality : {0, 51})
                    {
                        request.quality = quality;
                        Require(an_export_run(context, &request, Render, nullptr, &frames, error.data(), error.size()) == 4,
                            "Valid CRF boundary rejected");
                    }
                    for (const auto quality : {-1, 52})
                    {
                        request.quality = quality;
                        Require(an_export_run(context, &request, Render, nullptr, &frames, error.data(), error.size()) == 1,
                            "Invalid active CRF accepted");
                    }
                }
                else
                {
                    for (const auto encodingMode : {0, 1})
                    {
                        request.encoding_mode = encodingMode;
                        for (const auto bitrate : {100000, 200000000})
                        {
                            request.video_bitrate = bitrate;
                            Require(an_export_run(context, &request, Render, nullptr, &frames, error.data(), error.size()) == 4,
                                "Valid CPU/GPU bitrate boundary rejected");
                        }
                        for (const auto bitrate : {99999, 200000001})
                        {
                            request.video_bitrate = bitrate;
                            Require(an_export_run(context, &request, Render, nullptr, &frames, error.data(), error.size()) == 1,
                                "Invalid active CPU/GPU bitrate accepted");
                        }
                    }
                }
            }
            for (const int codec : {3, 4, 5})
            {
                request.codec = codec; request.encoding_mode = 0;
                request.rate_control_mode = codec == 3 ? 4 : 1;
                request.video_bitrate = 8000000;
                for (const int quality : {codec == 5 ? 1 : 0, 63})
                {
                    request.quality = quality;
                    Require(an_export_run(context, &request, Render, nullptr, &frames, error.data(), error.size()) == 4,
                        "New codec quality boundary rejected");
                }
                request.quality = 64;
                Require(an_export_run(context, &request, Render, nullptr, &frames, error.data(), error.size()) == 1,
                    "New codec invalid quality accepted");
                if (codec == 5)
                {
                    request.quality = 0;
                    Require(an_export_run(context, &request, Render, nullptr, &frames, error.data(), error.size()) == 1,
                        "SVT CRF sentinel zero accepted");
                }
            }
            request.codec = 0;
            an_export_mux_request mux{sizeof(mux), AN_EXPORT_ABI_VERSION, "unused.mkv", nullptr, -1, 0};
            Require(an_export_mux(context, &mux, error.data(), error.size()) == 1, "Mux allowed before encoded completion");
            mux.reserved = 1;
            Require(an_export_mux(context, &mux, error.data(), error.size()) == 1, "Mux reserved field accepted");
            request.encoding_mode = 0;
            request.rate_control_mode = 1;
            request.quality = 20;
            request.video_bitrate = -100;
            an_export_destroy(context);
            an_export_destroy(context);
            Require(an_export_run(context, &request, Render, nullptr, &frames, error.data(), error.size()) == 1,
                "Destroyed handle was accepted");
            Require(an_export_get_result_info(context, &info, error.data(), error.size()) == 1,
                "Destroyed result information handle accepted");
        }
        std::cout << "PASS ABI 7, overlay POD24, explicit CRF/VBR/CBR, active quality boundaries, inactive fields, pinned runtime, cancellation, ownership\n";
        return 0;
    }
    catch (const std::exception &error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}
