#ifndef AEGINEXT_EXPORT_H
#define AEGINEXT_EXPORT_H
#include <stdint.h>
#if defined(_WIN32)
#define AN_EXPORT_CALL __cdecl
#ifdef AEGINEXT_EXPORT_BUILD
#define AN_EXPORT_API __declspec(dllexport)
#else
#define AN_EXPORT_API __declspec(dllimport)
#endif
#else
#define AN_EXPORT_CALL
#define AN_EXPORT_API __attribute__((visibility("default")))
#endif
#ifdef __cplusplus
extern "C" {
#endif
/* ABI 6; one run per context. Cancel may run concurrently; destruction may not. */
typedef struct an_export_request
{
    uint32_t struct_size, abi_version;
    int32_t video_stream_index, codec; /* 0 auto, 1 H.264 SDR, 2 HEVC 10-bit, 3 VP8, 4 VP9, 5 AV1 */
    int32_t quality;
    uint32_t width, height, flags;
    const char *input_path, *output_path, *preset;
    float reference_white_nits;
    uint32_t reserved;
    int32_t encoding_mode; /* 0 software, 1 required hardware video encoding */
    int32_t video_bitrate; /* bits/second; active for CQ/VBR/CBR, normalized to whole kilobits/second */
    uint32_t decode_mode; /* 0 automatic, 1 software, 2 required hardware decoding */
    uint32_t decode_reserved;
    int32_t rate_control_mode; /* 1 CRF (software only), 2 VBR, 3 CBR, 4 CQ (VP8 software); automatic must be resolved by caller */
    uint32_t rate_control_reserved;
} an_export_request;
typedef struct an_export_rational
{
    int32_t numerator, denominator;
} an_export_rational;
typedef struct an_export_hdr_metadata
{
    uint32_t flags, reserved; /* 1 mastering primaries, 2 mastering luminance, 4 content light */
    an_export_rational red_x, red_y, green_x, green_y, blue_x, blue_y, white_x, white_y;
    an_export_rational min_luminance, max_luminance;
    uint32_t max_cll, max_fall;
} an_export_hdr_metadata;
typedef struct an_export_mux_request
{
    uint32_t struct_size, abi_version;
    const char *output_path, *audio_path;
    int32_t audio_stream_index; /* null audio_path and -1 selects no audio */
    uint32_t reserved;
} an_export_mux_request;
/* Successful export diagnostics and effective encoded color. Source facts remain unchanged. */
typedef struct an_export_result_info
{
    uint32_t struct_size, abi_version, core_version, capabilities;
    uint32_t requested_decode_mode, active_decode_backend, hardware_confirmed, inferred_fields;
    uint64_t generation, delivered_frames;
    int32_t color_range, color_matrix, color_primaries, color_transfer, chroma_location, alpha_mode;
    char fallback_reason[256];
    int32_t rate_control_mode; /* successfully initialized mode: 1 CRF, 2 VBR, 3 CBR */
    int32_t video_bitrate; /* initialized target bits/second; zero for CRF */
    int32_t quality; /* initialized quality; zero for VBR/CBR */
    uint32_t verification; /* 1 configured and initialized, 2 queried through public driver API */
    int32_t codec, bit_depth;
    char pixel_format[32];
    an_export_hdr_metadata hdr;
    uint32_t output_verification; /* 1 encoded bitstream checked, 2 final container checked */
    uint32_t bitrate_precision; /* rate-control target quantum in bits per second */
} an_export_result_info;
typedef struct an_export_overlay_info
{
    uint32_t struct_size, abi_version, state, reserved;
    uint64_t revision;
} an_export_overlay_info;
enum an_export_overlay_state
{
    AN_EXPORT_OVERLAY_UPDATED = 1,
    AN_EXPORT_OVERLAY_UNCHANGED = 2,
    AN_EXPORT_OVERLAY_EMPTY = 3
};
/* Updated fully overwrites tight premultiplied linear-sRGB float RGBA. Unchanged reuses the last
   successful revision. Empty ignores RGBA. Revision must be nonzero; return 0 OK, 1 cancelled, 2 error.
   Native initializes info to size 24, ABI 6, state/reserved/revision 0. The buffer belongs to native,
   remains unchanged on Unchanged, and may not be retained by the callback. */
typedef int32_t (AN_EXPORT_CALL *an_export_render_callback)(void *user, int64_t pts,
    int32_t time_base_num, int32_t time_base_den, uint32_t width, uint32_t height,
    float *rgba, uint64_t channels, an_export_overlay_info *info);
AN_EXPORT_API uint32_t AN_EXPORT_CALL an_export_abi_version(void);
AN_EXPORT_API uint32_t AN_EXPORT_CALL an_export_core_version(void);
AN_EXPORT_API uint32_t AN_EXPORT_CALL an_export_capabilities(void);
AN_EXPORT_API int32_t AN_EXPORT_CALL an_export_create(void **context, char *error, uint32_t capacity);
AN_EXPORT_API void AN_EXPORT_CALL an_export_cancel(void *context);
AN_EXPORT_API void AN_EXPORT_CALL an_export_destroy(void *context);
/* Owned by context; available after encoder initialization, empty before it. */
AN_EXPORT_API const char *AN_EXPORT_CALL an_export_encoder_name(void *context);
AN_EXPORT_API int32_t AN_EXPORT_CALL an_export_get_result_info(void *context, an_export_result_info *info,
    char *error, uint32_t capacity);
/* 0 OK, 1 invalid argument, 2 unsupported, 3 failure, 4 cancelled. */
AN_EXPORT_API int32_t AN_EXPORT_CALL an_export_mux(void *context, const an_export_mux_request *request,
    char *error, uint32_t capacity);
AN_EXPORT_API int32_t AN_EXPORT_CALL an_export_run(void *context, const an_export_request *request,
    an_export_render_callback render, void *user, uint64_t *frames, char *error, uint32_t capacity);
#ifdef __cplusplus
}
static_assert(sizeof(an_export_request) == 88);
static_assert(sizeof(an_export_result_info) == 488);
static_assert(sizeof(an_export_hdr_metadata) == 96);
static_assert(sizeof(an_export_mux_request) == 32);
static_assert(sizeof(an_export_overlay_info) == 24);
#endif
#endif
