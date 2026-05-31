using System;
using System.IO;
using System.Runtime.InteropServices;

namespace ImageTool.Imaging;

/// <summary>
/// Lớp interop tới LibRaw (C API) — demosaic sensor RAW thật (12-14 bit). TUỲ CHỌN: chỉ hoạt động
/// khi <c>libraw.dll</c> có mặt cạnh app; nếu không, <see cref="Available"/> = false và mọi thứ rơi
/// về <see cref="RawPreviewDecoder"/> (JPEG preview nhúng).
///
/// Thiết kế an toàn: phát hiện DLL bằng <see cref="NativeLibrary.TryLoad"/> (giống OnnxUpscaler),
/// bọc mọi P/Invoke trong try/catch, không bao giờ ném ra ngoài <see cref="TryDecode"/>. Cấu hình
/// xuất linear-gamma + primaries sRGB để khớp pipeline (gamma[0]=1, gamma[1]=1, output_color=1,
/// no_auto_bright=1, output_bps=16).
///
/// LƯU Ý: chữ ký P/Invoke dựa trên LibRaw C API ổn định (libraw_init/open/unpack/dcraw_process/
/// dcraw_make_mem_image). Cần verify trên máy có libraw.dll thật — phần managed (convert) đã test riêng.
/// </summary>
public static class LibRawNative
{
    private const string Dll = "libraw";

    private static readonly bool _available = DetectAvailable();

    /// <summary>True nếu libraw.dll nạp được (RAW demosaic thật khả dụng).</summary>
    public static bool Available => _available;

    private static bool DetectAvailable()
    {
        try
        {
            // Tìm cạnh app trước (bundled), rồi mới để OS resolver thử.
            string baseDir = AppContext.BaseDirectory;
            foreach (var name in new[] { "libraw.dll", "raw.dll", "libraw_r.dll" })
            {
                var p = Path.Combine(baseDir, name);
                if (File.Exists(p) && NativeLibrary.TryLoad(p, out _)) return true;
            }
            return NativeLibrary.TryLoad(Dll, out _);
        }
        catch { return false; }
    }

    // ===== LibRaw C API (libraw_c_api.h) =====
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr libraw_init(uint flags);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern int libraw_open_file(IntPtr lr, [MarshalAs(UnmanagedType.LPStr)] string file);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern int libraw_unpack(IntPtr lr);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern int libraw_dcraw_process(IntPtr lr);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr libraw_dcraw_make_mem_image(IntPtr lr, out int errc);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern void libraw_dcraw_clear_mem(IntPtr img);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern void libraw_close(IntPtr lr);

    // Con trỏ tới các trường tham số xử lý (libraw_output_params_t) để bật linear-gamma.
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern void libraw_set_gamma(IntPtr lr, int index, float value);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern void libraw_set_output_color(IntPtr lr, int color);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern void libraw_set_output_bps(IntPtr lr, int bps);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern void libraw_set_no_auto_bright(IntPtr lr, int value);

    // D5.2: chọn thuật toán demosaic (user_qual), highlight mode, WB nhân hệ số (user_mul) + đọc cam_mul.
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern void libraw_set_demosaic(IntPtr lr, int value);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern void libraw_set_highlight(IntPtr lr, int value);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern void libraw_set_user_mul(IntPtr lr, int index, float val);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern float libraw_get_cam_mul(IntPtr lr, int index);

    // libraw_processed_image_t header (đầu struct): type, height, width, colors, bits, data_size.
    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessedImageHeader
    {
        public int Type;       // LIBRAW_IMAGE_JPEG=1, LIBRAW_IMAGE_BITMAP=2
        public ushort Height;
        public ushort Width;
        public ushort Colors;
        public ushort Bits;
        public uint DataSize;
        // sau header là mảng byte data[] (lấy bằng offset).
    }

    /// <summary>
    /// Decode 1 file RAW thành (pixel bytes, width, height, colors, bits). Trả null nếu không khả dụng,
    /// hoặc bất kỳ lỗi nào (caller fallback JPEG preview). Pixel đã ở linear-gamma + sRGB primaries.
    /// </summary>
    public static RawBitmap? TryDecode(string path, RawOptions? options = null)
    {
        if (!_available) return null;
        var opt = options ?? RawOptions.Default;
        IntPtr lr = IntPtr.Zero;
        IntPtr img = IntPtr.Zero;
        try
        {
            lr = libraw_init(0);
            if (lr == IntPtr.Zero) return null;

            // Cấu hình best-effort: gamma tuyến tính (1/1), output sRGB, 16-bit, tắt auto-bright.
            // Bọc riêng vì 1 vài build LibRaw thiếu setter -> bỏ qua, vẫn decode bằng mặc định.
            TryConfigure(lr, opt);

            if (libraw_open_file(lr, path) != 0) return null;
            if (libraw_unpack(lr) != 0) return null;

            // WB as-shot (D5.2): đọc cam_mul rồi đặt lại làm user_mul. Phải sau unpack (metadata đã đọc),
            // trước dcraw_process. Build LibRaw này không có set_use_camera_wb nên dùng cách user_mul.
            if (opt.UseCameraWhiteBalance)
                TryApplyCameraWb(lr);

            if (libraw_dcraw_process(lr) != 0) return null;

            img = libraw_dcraw_make_mem_image(lr, out int errc);
            if (img == IntPtr.Zero || errc != 0) return null;

            var hdr = Marshal.PtrToStructure<ProcessedImageHeader>(img);
            if (hdr.Type != 2 || hdr.Width <= 0 || hdr.Height <= 0 || hdr.DataSize == 0)
                return null; // chỉ nhận bitmap (type 2)

            // data nằm ngay sau header (16 byte: int + 4*ushort + uint = 4+8+4 = 16).
            int headerSize = Marshal.SizeOf<ProcessedImageHeader>();
            var bytes = new byte[hdr.DataSize];
            Marshal.Copy(img + headerSize, bytes, 0, (int)hdr.DataSize);

            return new RawBitmap
            {
                Pixels = bytes,
                Width = hdr.Width,
                Height = hdr.Height,
                Colors = hdr.Colors,
                Bits = hdr.Bits,
            };
        }
        catch
        {
            return null; // mọi lỗi native -> fallback
        }
        finally
        {
            try { if (img != IntPtr.Zero) libraw_dcraw_clear_mem(img); } catch { }
            try { if (lr != IntPtr.Zero) libraw_close(lr); } catch { }
        }
    }

    // Đặt tham số xuất linear-gamma/sRGB/16-bit + demosaic/highlight (D5.2). Mỗi setter bọc riêng:
    // build LibRaw thiếu 1 export (EntryPointNotFoundException) không làm hỏng cả decode.
    private static void TryConfigure(IntPtr lr, RawOptions opt)
    {
        try { libraw_set_gamma(lr, 0, 1.0f); libraw_set_gamma(lr, 1, 1.0f); } catch { }
        try { libraw_set_output_color(lr, 1); } catch { } // 1 = sRGB
        try { libraw_set_output_bps(lr, 16); } catch { }
        try { libraw_set_no_auto_bright(lr, 1); } catch { }
        // Demosaic: 0=linear,1=VNG,2=PPG,3=AHD,4=DCB,11=DHT,12=AAHD. Clamp an toàn.
        try { libraw_set_demosaic(lr, Math.Clamp(opt.DemosaicQuality, 0, 12)); } catch { }
        // Highlight: 0=clip,1=unclip,2=blend,3..9=rebuild. Clamp.
        try { libraw_set_highlight(lr, Math.Clamp(opt.HighlightMode, 0, 9)); } catch { }
    }

    // Áp WB as-shot: đọc 4 hệ số cam_mul rồi gán làm user_mul (chuẩn hoá để không lệch sáng).
    private static void TryApplyCameraWb(IntPtr lr)
    {
        try
        {
            var mul = new float[4];
            for (int i = 0; i < 4; i++) mul[i] = libraw_get_cam_mul(lr, i);
            // cam_mul hợp lệ: ít nhất R/G/B > 0. Nếu toàn 0 (không có metadata) -> bỏ qua.
            if (mul[0] <= 0f || mul[1] <= 0f || mul[2] <= 0f) return;
            // mul[3] có thể = 0 (cảm biến 3 màu) -> dùng mul[1] (G) thay.
            if (mul[3] <= 0f) mul[3] = mul[1];
            for (int i = 0; i < 4; i++) libraw_set_user_mul(lr, i, mul[i]);
        }
        catch { /* thiếu export -> bỏ qua, dùng WB mặc định của LibRaw */ }
    }

    /// <summary>Tuỳ chọn decode RAW (D5.2): WB as-shot, thuật toán demosaic, xử lý highlight.</summary>
    public sealed class RawOptions
    {
        /// <summary>Dùng WB as-shot từ metadata máy (true) hay WB mặc định LibRaw (false).</summary>
        public bool UseCameraWhiteBalance { get; init; } = true;
        /// <summary>Thuật toán demosaic LibRaw (user_qual): 3=AHD (mặc định, cân bằng), 11=DHT (sắc), 4=DCB.</summary>
        public int DemosaicQuality { get; init; } = 3;
        /// <summary>Highlight mode: 0=clip, 1=unclip, 2=blend, 3..9=rebuild.</summary>
        public int HighlightMode { get; init; } = 0;

        public static readonly RawOptions Default = new();
    }

    /// <summary>Kết quả decode RAW thô từ LibRaw.</summary>
    public sealed class RawBitmap
    {
        public required byte[] Pixels { get; init; }
        public int Width { get; init; }
        public int Height { get; init; }
        public int Colors { get; init; }
        public int Bits { get; init; }
    }
}
