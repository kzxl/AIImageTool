# AIImageTool

AIImageTool là ứng dụng Desktop (WPF, .NET 8) xử lý & nâng cấp ảnh, kết hợp **chỉnh sửa phi phá hủy
kiểu Lightroom/Darktable** với các luồng **AI** (upscale, khôi phục khuôn mặt, auto-tag). Kiến trúc gồm
một **pipeline Develop linear-light** trong lõi và một lớp **Plugin mở rộng** cho các tác vụ AI nặng.

## Kiến trúc tổng quan

- **`ImageTool.Core`** — interface & model dùng chung (workspace, history, catalog, meta, style...).
- **`ImageTool.Imaging`** — pipeline Develop phi phá hủy chạy ở **linear light** (float RGBA):
  `LinearImage`, `ColorSpace`, `IEditOp`/`EditOpRegistry`, `EditPipeline` + `CachedEditPipeline`
  (cache theo tầng, replay từ op bị đổi), `ImageDecoderRegistry`. ~40 op chỉnh sửa, đều có unit test.
- **`ImageTool.Shared`** — dịch vụ nền: workspace, history, catalog SQLite, thumbnail, batch, style,
  export, EXIF/GPS, keyword, stacking, filename token, logging.
- **`ImageTool.Host`** — UI WPF: top-bar, browser/filmstrip, CenterPreview (Single/Grid/Cull/Full +
  crop/brush/compare/clipping overlay) và panel phải dạng tab (Develop / Info / History / Style /
  Batch / Export / Tools).
- **Plugins** (`.dll` hot-load từ thư mục `Plugins`): Upscaler, FaceRestorer, VisionTagger.

## Tính năng chính

### Develop — chỉnh sửa phi phá hủy (linear light)
- **Tone:** Exposure, Contrast, Highlights/Shadows/Whites/Blacks, Tone Curve (kéo điểm + **preset
  Linear/Medium/Strong/Faded**), Parametric Curve, Filmic/Filmic RGB/Sigmoid/Tone Equalizer, Dehaze,
  Levels (per-channel + **Auto Levels** + **Auto Color** khử ám), Highlight Reconstruction, Auto Tone,
  **kéo trực tiếp trên histogram để chỉnh tone**.
- **Color:** White Balance (Kelvin + **Auto WB** + **eyedropper** + **preset nguồn sáng**), HSL 8 dải,
  Color Balance RGB 4-way + Color Grading wheel, Split Toning, Channel Mixer, Selective Color, Color Unify,
  Velvia, Color Contrast (Lab), 3D LUT (.cube), Input color profile (sRGB/AdobeRGB/Rec2020/P3),
  **Black & White** (channel mix + **filter màu cổ điển** + toning), **Negative/Invert**, **Film Negative
  (negadoctor)** cho scan phim âm bản.
- **Detail:** Sharpen (+ Radius/**Masking** edge-aware), Luminance/Color/Chroma Noise Reduction,
  Diffuse-or-sharpen (PDE), Hot Pixel, CA Correct, Defringe, Texture, Clarity, **Grain (mono + màu)**.
- **Geometry:** Crop (kéo khung + **preset tỉ lệ** 1:1/16:9... + guide bố cục), Straighten, Rotate/Flip + **EXIF
  auto-orientation**, **Perspective/Upright**, **Liquify/Warp** (kéo handle), Lens Correction (thủ công +
  **lensfun tự động** theo EXIF).
- **Effects:** Vignette (+ Roundness/Highlights), Glow/Orton.
- **Local Adjustments:** mask Gradient / Radial / Brush / Polygon / Luminance & Color Range / Parametric
  đa kênh / AI Subject / Sky, blend modes + opacity, mask combine, **nhân bản mask**; mỗi mask đầy đủ slider.
- **Preset/Style:** lưu/áp style (append/replace, chọn module), copy-paste settings (selective module),
  **import preset Lightroom (.xmp)**, XMP sidecar, **Named Snapshots** (lưu nhiều mốc edit có tên).

### Thư viện & catalog
- Workspace browser (cây thư mục + grid thumbnail), filmstrip, rating/flag/color label (**gắn hàng loạt
  cho cả selection**), **lọc Pick/Reject/Hide-rejected**.
- Catalog SQLite: import, **smart collections** (lọc theo rule), **tìm kiếm nâng cao** (camera/lens/ISO/
  khẩu độ/tiêu cự/ngày), **keyword phân cấp** + editor chip + **từ điển/recently-used tag**, **stacking**.

### Panel Info (đã gộp)
- Histogram RGB/Luma + cảnh báo clip, **dòng tóm tắt chụp** (camera · tiêu cự · f · tốc độ · ISO),
  **bảng màu chủ đạo K-Means** (click copy hex), **GPS → mở bản đồ**, **sửa EXIF** trực tiếp
  (Description/Artist/Copyright/Software/Make/Model), và **trình sửa Keywords** (chip thêm/gỡ + gợi ý).

### Export
- PNG/JPEG/WebP/TIFF (8/16-bit), resize %/cạnh dài, watermark, **sharpen-for-output**,
  **filename token** (`{name}/{n:000}/{date}/{parent}...`), **export presets**, batch song song,
  **giữ EXIF gốc** (camera/lens/ngày/GPS), **không ghi đè im lặng** (tự thêm hậu tố khi trùng tên).

### AI Plugins
- **Upscaler:** 4x-UltraSharpV2 (ONNX), Tiled Inference + DirectML (NVIDIA/AMD/Intel), fallback CPU.
- **Face Restorer:** GFPGAN (ONNX) khôi phục chân dung.
- **Vision Tagger:** auto caption + tag (WD ViT), lưu thẳng vào keyword của ảnh.

## Yêu cầu hệ thống
- Windows 10/11 (64-bit).
- Bản *Lite*: cần Microsoft .NET 8 Desktop Runtime.
- **Bắt buộc:** [Visual C++ 2015-2022 Redistributable (x64)](https://aka.ms/vs/17/release/vc_redist.x64.exe) cho thư viện AI Native.
- Khuyên dùng GPU tương thích DirectML (NVIDIA, AMD Radeon, Intel UHD).

## Cài đặt từ Release
Vào [Releases](../../releases):
1. `ImageTool_Lite_Win_x64.zip` — máy đã cài .NET 8.
2. `ImageTool_Full_Win_x64.zip` — trọn bộ, chạy trực tiếp không cần cài đặt.

## Phát triển
- Build: `dotnet build ImageTool.slnx -c Debug`
- Test: `dotnet test ImageTool.Tests/ImageTool.Tests.csproj` (544 test, build 0 warning)
- Publish: `pwsh ./publish.ps1` (Lite + Full + plugins)
- Hướng dẫn viết op Develop mới: `ImageTool.Imaging/WRITING_OPS.md`

## License
Apache License 2.0 — xem [LICENSE](LICENSE).
