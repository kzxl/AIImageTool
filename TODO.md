# ImageTool - TODO & Roadmap

> File theo dõi tiến độ bền vững. Mục tiêu: đạt feature-parity với Lightroom + Darktable,
> tối ưu hiệu suất, và cải thiện UX/UI. Cập nhật mỗi khi xong 1 mục (đổi `[ ]` -> `[x]`).
>
> Cập nhật lần cuối: 2026-06-01 (d) — RÀ SOÁT & HOÀN THIỆN UI (op có engine nhưng thiếu/cụt UI):
> (1) **Gradient Map** — thêm color picker hex Shadow/Mid/High tuỳ chỉnh + slider Midpoint (trước chỉ có
> preset + opacity; engine vốn hỗ trợ màu/midpoint tuỳ ý nhưng UI hardcode); preset giờ chỉ điền sẵn hex.
> (2) **ChannelMixer (Color Calibration)** — op trước MỒ CÔI (đăng ký nhưng 0 UI), nay có nhóm "Color
> Calibration" 6 slider hue/sat từng primary R/G/B + BuildOps + Load. (3) **Input Profile "Embedded ICC"** —
> thêm lựa chọn áp ma trận colorant ICC nhúng THẬT (`IccProfileReader.TryReadRgbToXyzD65FromFile` →
> `InputProfileOp.SourceMatrix`), dùng được profile lạ không khớp tên. (4) **PathMask per-node feather** —
> mỗi node ghi giá trị Feather hiện tại lúc click (đổi slider giữa các lần click → mép mềm/cứng khác nhau
> theo node, đúng "path" Darktable). 799 test pass, build 0 warning.
> 2026-06-01 (c) — ĐỢT TÍNH NĂNG TEST-ĐƯỢC: (#6) Multi-page Print
> (`PrintModule.RenderPages`/`PageCount`/`CellsPerPage`: chia ảnh tràn nhiều trang, tự đánh số _p01/_p02);
> (#3) `PathMask` (D4.3 đầy đủ — path nhiều node + feather RIÊNG từng node, nội suy theo node gần nhất;
> nối DevelopPanel "+ Path" + CenterPreview click đặt node); (#2) `LocalToneMapOp` (HDR-look single-shot:
> tách log-luminance base/detail, nén dải động giữ tương phản cục bộ; UI "Local Tone (HDR)" trong Tone
> Mapping); (#1) Soft-proof — `GamutMapOp` (clip/desaturate về gamut đích sRGB/AdobeRGB/Rec2020/P3 hoặc
> ma trận ICC bất kỳ) + `GamutCheck` (phát hiện màu ngoài gamut + % pixel) + UI "Soft Proof / Proof Mode"
> trong Color Management; (#5) `InputProfileOp.SourceMatrix` — input profile theo ma trận RGB→XYZ tuỳ
> chỉnh (nền tảng DCP/camera matrix, dùng được colorant ICC thật). 799 test pass, build 0 warning.
> 2026-06-01 (b) — PRINT MODULE (#11): `PrintModule` dựng file raster sẵn-sàng-in theo
> khổ giấy (A3/A4/A5/Letter/Legal/4×6/5×7/8×10) + DPI (150/200/300) + orientation + lề/khoảng cách (mm)
> + lưới N-up (Rows×Cols) + Fit/Fill; bố cục PageLayout thuần toán học (mm→px theo DPI) tách riêng để test
> (9 test: A4@300=2480×3508, landscape swap, grid margin/gap, render single + N-up + skip ảnh lỗi). Nối
> ExportPanel: nút "In ấn (Print)…" + `PhotoPrintDialog` (code-built) -> render -> mở file. 765 test pass, build 0 warning.
> 2026-06-01 (a) — ĐỢT ĐỘ BỀN (A): (A1) Plugin load/init/UI lỗi báo cho user qua toast +
> AppLog thay vì nuốt im lặng (PluginLoader.LoadErrors). (A2) AuraSR worker thêm endpoint /health + C#
> ping health-check timeout ngắn trước khi gửi (hết treo 3 phút khi worker chưa chạy/model chưa nạp).
> (A3) Model discovery bền: `ModelLocator` tìm đa vị trí (publish + build cục bộ + đệ quy Plugins\) + ghi log
> khi thiếu model (hết tình trạng pipeline trả ảnh y nguyên IM LẶNG); dùng chung cho UI/Batch/pipeline.
> (A4) Catalog lưu curation (Rating/Label/Pick/Keywords) đồng bộ từ sidecar .imgtool.json: populate lúc
> import + tự đồng bộ qua MetaChanged -> UpdateCuration; Advanced Search + Smart Collection lọc/sort được
> theo rating/label/pick/keyword (SmartCollectionDialog thêm 4 trường). 756 test pass, build 0 warning.
> 2026-05-31 (b) — ĐỢT TÍNH NĂNG LỚN: Gradient Map (#5) + Color Match Reinhard (#8) +
> Perceptual hash/duplicate finder (#1) + Auto-Upright keystone (#6) + Frequency Separation skin (#7) +
> Delta-E CIE76/CIEDE2000 (#9) + Social media presets (#10) + Import from URL + Auto-tag EXIF (#3) +
> Web gallery HTML (#11) + **Panorama stitching đầy đủ** (Homography DLT/RANSAC + Harris/NCC + warp/blend, #4) +
> Auto-save XMP debounce (#12) + Watched folder auto-import (#2). 755 test pass, build 0 warning.
> 2026-05-31 (a) — Output ICC profile khi export (IccProfileWriter), Watermark resize-resilient,
> History thumbnail từng bước, Toolbar Tools menu. Nén sâu Squoosh-style (per-format encoder + target-size +
> strip meta), Watermark vô hình (blind DCT+QIM), Smart Crop content-aware, UX pass (arrow-nav, Esc/Enter crop,
> empty states, slider tooltips, cheat-sheet F1), ICC colorant matrix parse + Bradford D50→D65 + MatchSpace gamut.
> 2026-05-30 — Healing brush, Lens correction, Sky mask, AI batch tag, AI Upscale op chuỗi,
> Space-to-pan, Light theme, PipelineProfiler. Darktable Đợt 1+2 HOÀN TẤT: blend modes (D4.5), Sigmoid +
> Filmic RGB + Tone Equalizer (D1), Color Balance RGB + Color Contrast + Levels + Velvia (D2), Parametric
> mask đa kênh (D4.1), Hot Pixel (D3.3) + CA Correct (D3.4) + Chroma denoise edge-aware (D3.2), Selective
> paste module (D6.1) + Style append (D6.2). Đợt 3: Diffuse-or-sharpen PDE (D3.1) + Input color profile matrix (D2.2) + Glow/Soften (Orton) + Tone Curve preserve-hue (D1.4) + Mask combine intersect/union/subtract (D4.2) + Polygon mask (D4.3) + Highlight reconstruction (D5.3) + **Liquify/Warp (D3.5: engine + UI kéo handle)** + **Cull nâng cao (D6.5: flag/rating/label hàng loạt + lọc Pick/Reject/Hide-rejected)** + **Tag dictionary/recent + Keyword editor (D6.4)** + **Histogram kéo chỉnh tone (13.10)** + **Sharpen Radius/Masking (4.1)** + **Nhớ bề rộng panel (11.10)** + **Auto Levels (D2.5)** + **Per-channel Levels (D2.5)** + **Film Negative/negadoctor (13.3)** + **Giữ EXIF khi export (9.4)** + **Vignette Roundness/Highlights (5.5)** + **Named Snapshots (D6.3 một phần)** + **Nhân bản mask (D4.4 một phần)** + **EXIF auto-orientation (5.2)** + **Tone Curve presets (2.2)** + **B&W color filters (13.1)** + **Grain Color/Size/Roughness UI (5.6)** + **WB preset nguồn sáng (3.1)** + **Auto Color khử ám (D2.5)** + **Export không ghi đè im lặng (9.4)** + **Waveform/RGB-Parade scope (11.3)** + **Catalog Collection tests (8.1)** + **Thumbnail cache-key tests (10.9)** + **Crop guides Thirds/Golden/Diagonal/Grid (5.1)** + **LR tone curve import tổng + per-channel (9.3)** + **LR Split Toning import (9.3)** + **LR HSL/Color Mixer import (9.3)** + **LR Color Grading + Texture import (9.3)** + **LR Grain import (9.3)** + **LibRaw RAW decoder scaffold (D5.1/D5.2, gated)** + **Light theme migration hoàn chỉnh (11.9)** + **LibRaw WB as-shot/demosaic (D5.2)** + **Lensfun auto lens-correction (5.3)** + **Đọc ICC nhúng + auto Input Profile (D2.2/7.3)** + **Parse ICC colorant matrix + Bradford D50→D65 + nhận diện gamut theo ma trận (D2.2/7.3)** + **Nén sâu Squoosh-style (EncoderFactory per-format + TargetSizeEncoder + strip metadata + UI)** + **Smart Crop content-aware** + **UX: arrow-nav/Esc-Enter crop/empty states/slider tooltips/cheat-sheet F1**. **Output ICC profile khi export (D2.2/9.8)** + **Watermark resize-resilient (9.7)** + **History thumbnail từng bước (11.11)** + **Toolbar Tools menu (Merge HDR/Focus Stack/Batch Rename)**. Dynamic range: Exposure Fusion (HDR merge) + Focus measure/stacking. **ĐỢT TÍNH NĂNG LỚN (2026-05-31b):** Gradient Map + Color Match (Reinhard Lab) + Perceptual hash/duplicate finder + Auto-Upright + Frequency Separation + Delta-E (CIEDE2000) + Social presets + Import URL + Auto-tag EXIF + Web gallery + **Panorama stitching đầy đủ** + Auto-save + Watched folder. 755/755 test pass, build 0 warning, 3 plugin.

---

## TIẾN ĐỘ (phiên gần nhất)

**ĐÃ XONG:**
- Build sạch **0 error, 0 warning**; pin SDK ổn định qua `global.json` (9.0.312); dọn toàn bộ 24 nullable warning.
- **PHẦN 1 — Nền tảng non-destructive linear-light**: `ImageTool.Imaging` (trước mồ côi) đã nối vào
  Host/Shared. `DevelopRenderer` (decode->proxy 2048px linear->EditPipeline->WriteableBitmap, off-thread
  + cancellation). Tab **Develop** với slider live (debounce 40ms) -> `UpsertGroup` -> re-render. Export
  full-res bake edits.
- **~33 op Develop (linear-light, đều có test):** DevelopBasic, ToneCurve, ParametricCurve, HslMixer (8 dải),
  Clarity, Texture, Sharpen, Dehaze, Filmic, SplitToning, ChannelMixer, ColorGrading (3-way), Vignette,
  Grain, ColorNR, LumaNR, Defringe, Orientation (rotate/flip), Crop/Straighten, AutoTone, **WB Kelvin**,
  **SelectiveColor**, **LUT .cube (3D)**.
- **Local Masking:** `MaskedOp` + LinearGradient / Radial / LuminanceRange — biến bất kỳ op global thành
  local adjustment. Replay qua pipeline.
- **Perf:** SIMD per-channel (Vector<float>) cho WB+Exposure; LUT 4096 encode sRGB nhanh; proxy + cache.
- **UX/UI:** Fix nhảy tab (header 1 hàng cuộn ngang). DevelopPanel nhóm thu gọn (Expander) + ô nhập số +
  double-click reset. Develop Presets (lưu/áp qua StyleService). Copy/Paste settings (Ctrl+Shift+C/V +
  context menu, paste nhiều ảnh). Context menu thumbnail (3 view). Badge "đã chỉnh sửa" trong grid/filmstrip.
  Before/After toggle giữ phím `\`. Status bar tiến trình. Nút xoay/lật/straighten.
- **Định dạng:** decode 16-bit (PNG16/TIFF16, đánh dấu IsHighBitDepth); export TIFF; watermark chữ;
  resize theo %; XMP sidecar (.xmp) cho edit.
- **Test:** 112/112 pass (`ImageTool.Tests`). Smoke test: app khởi động sạch, không crash.
- **Doc:** `ImageTool.Imaging/WRITING_OPS.md` — hướng dẫn viết op mới.
- **Thêm UX:** histogram cảnh báo clip (marker + %), undo/redo nhãn op ở status bar, toast notification
  không chặn (batch/export xong), WB Kelvin slider, Selective Color + 3D LUT (.cube) trong DevelopPanel.
- **Phiên 2026-05-29 (b):** thêm 4 tính năng phi phá hủy mới (đều có test, build 0 warning):
  - **Color Unify** (`ColorUnifyOp`, 3.7) — kéo hue toàn ảnh về 1 tông màu, giữ luminance.
  - **Color Range Mask** (`ColorRangeMask`, 6.5) — chọn vùng theo hue±range + ngưỡng sat.
  - **Brush Mask** (`BrushMask`, 6.4) — mask từ chuỗi chấm toạ độ chuẩn hoá (Radius/Hardness).
  - **Perspective / Upright** (`PerspectiveOp`, 5.4) — homography keystone V/H + Rotate + Scale.
  Tất cả nối vào DevelopPanel (Color Unify group, Color/Brush mask qua MaskedOp, Perspective trong Geometry).
- **Phiên 2026-05-29 (c) — UI tương tác + perf + dọn dẹp (build 0 warning, 112/112 test):**
  - **Tone Curve editor** (2.2, `CurveEditor` + `CurveMath` dùng chung op) — kéo điểm/thêm/xoá.
  - **Color Grading 3-way wheels** (3.3, `ColorWheel`).
  - **Crop chữ nhật kéo tay** (5.1, `CenterPreview.Crop`) — overlay khung + tay nắm + thirds, phím R.
  - **Brush vẽ tay trên canvas** (6.4 UI, `CenterPreview.Brush`).
  - **Local Adjustments đầy đủ slider/mask** (6.7, `LocalMask` + DevelopPanel.Masks) — thêm/xoá mask,
    mỗi mask 12 slider Light/Color, hỗ trợ Gradient/Radial/Brush/LumRange/ColorRange.
  - **Sharpen-for-output** khi export (9.4).
  - **Cache theo tầng** (10.6, `CachedEditPipeline`) — replay từ op bị đổi.
  - **Gỡ `ImageTool.Worker.Upscaler`** dead code (12.2).

**CẦN DEPENDENCY NGOÀI (một phần đã làm qua model auto-tải/preview):**
- RAW decode: ĐÃ mở/xem qua JPEG preview nhúng (7.1). Demosaic sensor thật (7.2-7.3) cần LibRaw native.
- AI mask Subject (6.6) + AI denoise (4.3): ĐÃ có pipeline + UI, cần model ONNX (auto-tải, verify inference trên máy có GPU).
- Sky/Background mask riêng: cần model phân lớp ngữ nghĩa.
- Lens correction (5.3): cần database lensfun.
- Import .dtstyle Darktable (9.3): khác format (LR .xmp đã xong).

**CÒN LẠI (không chặn, giá trị biên):** zoom/pan loupe nâng cao (một phần đã có);
histogram tương tác kéo chỉnh tone (11.3); side-by-side before/after (11.4);
catalog nâng cao (8.x); GPU compute (10.7); ALC riêng cho plugin (10.10); light theme (11.9);
history panel có thumbnail từng bước (11.11).

**CẦN DEPENDENCY NGOÀI:** RAW decode (7.x, LibRaw), AI mask Subject/Sky (6.6, ONNX segmentation),
lens correction (5.3, lensfun), import XMP/.dtstyle của LR/Darktable (9.3).

---

## 0. Trạng thái hiện tại (baseline)

- Build: **OK (0 errors)**. Đã fix warning `CS0067` và race-condition copy plugin DLL.
- Stack: .NET 8, WPF, ImageSharp 3.1.12, ONNX Runtime DirectML (AI), SQLite catalog.
- Tính năng đang có: WorkspaceBrowser (cây thư mục + thumbnail grid), CenterPreview
  (Single/Grid/Cull/Full + before/after splitter), Filmstrip, History UI, Style, Batch,
  Export, Info (EXIF + histogram). Plugins: ColorLab, Upscaler, FaceRestorer, VisionTagger,
  MetaEditor.

### Phát hiện kiến trúc quan trọng
- **`ImageTool.Imaging` (pipeline linear-light non-destructive) đã viết xong nhưng KHÔNG
  được dự án nào tham chiếu.** Đây là nền tảng đúng cho toàn bộ tính năng Develop. Có sẵn:
  `LinearImage` (float RGBA linear), `ColorSpace` (sRGB<->linear), `IEditOp`/`EditOpRegistry`
  (op thuần tham số, replay được), `EditPipeline` (render full-res + proxy theo `scale`),
  `ImageDecoderRegistry` (chừa sẵn chỗ cho RAW). `BasicOps` mới có 5 op.
- **ColorLab hiện sửa phá hủy 8-bit sRGB + re-encode PNG mỗi lần chỉnh** -> trần chất lượng
  và hiệu suất. Cần chuyển sang `IEditOp` linear.
- **History hiện chỉ là nhật ký** (ghi op nhưng không có renderer replay). Undo/redo chỉ dời
  con trỏ, không render lại.

---

## 1. NỀN TẢNG (làm trước tất cả - blocker cho mọi tính năng Develop)

> Không có bước này thì mọi tool Lightroom/Darktable bên dưới chỉ là chỉnh phá hủy 8-bit.
> Đây là việc đòn bẩy cao nhất.
>
> **TRẠNG THÁI: HOÀN TẤT (2026-05-29).** Pipeline non-destructive linear-light đã chạy,
> 10/10 unit test pass. Tab "Develop" mới với slider live đã nối vào CenterPreview + History.

- [x] **1.1** Cho `ImageTool.Host` + plugins tham chiếu `ImageTool.Imaging` (Host + Shared đã ref).
- [x] **1.2** `DevelopBasicOp` (composite op linear) + đăng ký vào `EditOpRegistry.CreateDefault()`.
      `DevelopRenderer` dùng `EditPipeline` + `ImageDecoderRegistry`.
- [x] **1.3** Nối `EditPipeline` vào `CenterPreview` làm live renderer:
      - decode -> `LinearImage` gốc (cache trong DevelopRenderer)
      - proxy thu nhỏ cạnh dài 2048px (box-average linear) cho preview realtime
      - render full-res khi export (ExportBatchAdapter bake edits)
- [x] **1.4** Nối `IHistoryService.HistoryChanged` -> `CenterPreview.RenderDevelopAsync` (undo/redo/set-pointer tự render lại).
- [x] **1.5** Edit live: slider kéo -> `IHistoryService.Upsert` (thay op tại chỗ, debounce 40ms).
- [~] **1.6** Migrate ColorLab sang `IEditOp` linear — CHƯA: ColorLab vẫn destructive, sẽ làm ở Phần 3.7.
- [x] **1.7** Hiển thị bằng `WriteableBitmap` (BGRA32, encode sRGB tại chỗ) thay re-encode PNG.
- [x] **1.8** Render off-UI-thread (`Task.Run`) + hủy job cũ bằng `CancellationToken`.

---

## 2. TÍNH NĂNG DEVELOP - Light/Tone (Lightroom Basic + Darktable)

> Tất cả là `IEditOp` linear-light, đăng ký vào `BasicOps.RegisterAll` hoặc module mới.

- [x] **2.1** Highlights / Shadows / Whites / Blacks (đã có trong `DevelopBasicOp`, theo luminance mask mềm).
- [x] **2.2** Tone Curve - RGB tổng + 3 kênh R/G/B riêng (`ToneCurveOp`, spline monotone-cubic). UI curve editor
      (`CurveEditor`: kéo điểm, double-click thêm/xoá, chuột phải xoá; dùng chung `CurveMath` với op nên khớp 100%).
      **+ Preset đường cong: Linear/Medium/Strong contrast/Faded (lifted blacks) cho kênh RGB master. + test.**
- [x] **2.3** Parametric Curve (`ParametricCurveOp`, 4 vùng Highlights/Lights/Darks/Shadows, có UI + test).
- [x] **2.4** Texture (`TextureOp`, high-pass bán kính nhỏ, scale-aware).
- [x] **2.5** Clarity (`ClarityOp`, local contrast bán kính lớn, bảo vệ vùng sáng/tối, scale-aware).
- [x] **2.6** Dehaze (`DehazeOp`, dark-channel prior + airlight, scale-aware, có UI + test).
- [x] **2.7** Filmic tone mapping (`FilmicOp`, ACES approx, nén highlight, có UI + test).
- [x] **2.8** Auto Tone (`AutoTone.Analyze`: histogram luminance -> exposure/contrast/whites/blacks/
      shadows/highlights). Nút "Auto" trong DevelopPanel. + test.
- [x] **2.9** Đường cong chịu `scale` đúng cho op có bán kính (clarity/texture).

---

## 3. TÍNH NĂNG DEVELOP - Color (Lightroom Color Mixer + Darktable)

- [x] **3.1** White Balance: Kelvin slider (`WBKelvinOp`) + **eyedropper** (click điểm xám -> `ChannelGainOp`)
      + **Auto WB** (gray-world). As-shot từ RAW metadata CHƯA (cần RAW decode). **+ Preset nguồn sáng
      (Daylight/Cloudy/Shade/Tungsten/Fluorescent/Flash) đặt Kelvin chuẩn.**
- [x] **3.2** HSL / Color Mixer 8 kênh (Hue/Sat/Lum cho Red..Magenta) — `HslMixerOp`, UI combo chọn dải + 3 slider.
- [x] **3.3** Color Grading (`ColorGradingOp`, Shadows/Midtones/Highlights/Global + Blending, có test). UI color-wheel
      (`ColorWheel` 3-way: kéo hue/sat trên vòng tròn cho Shadows/Midtones/Highlights/Global + lum slider).
- [x] **3.4** Split Toning (`SplitToningOp`, HL/SH hue+sat + balance, có UI + test).
- [x] **3.5** Vibrance (saturation thông minh, bảo vệ vùng rực) — đã có trong `DevelopBasicOp`.
- [x] **3.6** Channel Mixer / Calibration (`ChannelMixerOp`, hue+sat 3 primary, có test).
- [~] **3.7** Port ColorLab sang pipeline non-destructive: SelectiveColor + LUT .cube + **Color Unify**
      (`ColorUnifyOp`, kéo hue toàn ảnh về 1 tông, giữ luminance, có UI + test) ĐÃ XONG. K-Means palette
      extraction (chỉ phân tích, không phải edit op) vẫn ở ColorLab cũ — không cần port.
- [x] **3.8** Color noise reduction (`ColorNoiseReductionOp`, tách chroma blur giữ luminance, có UI + test).

---

## 4. TÍNH NĂNG DEVELOP - Detail (Sharpen + Noise)

- [x] **4.1** Sharpening (`SharpenOp`, unsharp mask: Amount/Radius/Threshold, scale-aware). **Detail/Masking
      nâng cao ĐÃ XONG:** slider Sharpen Radius + Sharpen Masking (mask theo độ lớn gradient — chỉ sharpen cạnh
      mạnh, bảo vệ vùng phẳng khỏi khuếch đại nhiễu, kiểu Lightroom). + test.
- [x] **4.2** Luminance noise reduction (`LumaNoiseReductionOp`, blur kênh Y giữ chroma + Detail, có UI + test).
- [~] **4.3** AI denoise (`AiDenoiseOp` + `AiOpHost` delegate + `OnnxDenoiser` SCUNet) cắm vào pipeline như
      op cuối chuỗi, chạy full-res khi export, slider "AI Denoise" trong Detail. Cần model ONNX (auto-tải).
      Tích hợp Upscaler/FaceRestorer như op chuỗi CHƯA.
- [x] **4.4** Defringe (`DefringeOp`, khử viền tím/lục theo hue, có UI + test).

---

## 5. TÍNH NĂNG DEVELOP - Geometry & Optics

- [x] **5.1** Crop & Straighten (`CropOp`: crop chữ nhật + xoay, có test). UI crop chữ nhật kéo tay
      (`CenterPreview.Crop`: overlay khung + 8 tay nắm + thirds + shade, đồng bộ 2 chiều với DevelopPanel,
      phím R bật/tắt, ảnh hiển thị chưa-cắt khi đang chỉnh) + Straighten slider. **+ Guide bố cục đổi được
      (phím O): Thirds / Golden ratio / Diagonals / Grid 4x4 / None.** **+ Smart Crop content-aware
      (`SmartCrop`: saliency gradient + skin + bias trung tâm -> khung tốt nhất cho tỉ lệ; nút "✨ Smart"
      trong thanh Crop, `DevelopRenderer.AnalyzeSmartCrop` trên proxy). + test.**
- [x] **5.2** Rotate/Flip 90° (`OrientationOp`, IResizingOp, nút xoay/lật trong UI + test). **EXIF auto-orientation:
      `ExifOrientation.Bake` áp cờ orientation (1..8) vào pixel lúc decode (ảnh chụp dọc không còn nằm ngang). + test.
      Thumbnail cũng `AutoOrient()` để khớp.**
- [~] **5.3** Lens Correction: `LensCorrectionOp` (distortion k1/k2 đa thức bán kính + bù vignette góc), thủ công,
      có UI slider + test. **Lensfun tự động ĐÃ XONG (phần managed):** `LensfunDatabase` (parse XML lensfun +
      so khớp tên lens theo EXIF + nội suy hệ số theo tiêu cự) + `LensProfileOp` (áp model poly3/poly5/ptlens +
      vignetting "pa"), có test. Cần thả DB lensfun XML + verify trên ảnh thật để bật full auto.
- [x] **5.4** Perspective / Upright (`PerspectiveOp`, homography 3x3: Vertical/Horizontal keystone + Rotate +
      Scale bù viền, inverse-map song tuyến, IResizingOp, có UI trong Geometry + test).
- [x] **5.5** Vignette (`VignetteOp`, post-crop: amount/midpoint/feather, smoothstep). **+ Roundness
      (hình elip/chữ nhật) + Highlights (bảo vệ vùng sáng khi tối rìa, kiểu Lightroom).** + test.
- [x] **5.6** Grain (`GrainOp` ĐÃ XONG: amount/size/roughness deterministic + test; UI có slider Grain).
      **+ Grain Color (chromatic grain), UI lộ Size/Roughness/Color. + test.**

---

## 6. LOCAL ADJUSTMENTS / MASKING (điểm mạnh lớn nhất của LR/Darktable)

> Kiến trúc: mỗi op có thể gắn 1 "mask" (float 0..1/pixel). `EditPipeline` nhân hiệu ứng theo mask.
> Cần mở rộng `IEditOp` để nhận mask tùy chọn.

- [x] **6.1** Hạ tầng mask: `IMaskGenerator` + `MaskedOp` (clone+blend theo mask 0..1), invert, qua pipeline + test.
- [x] **6.2** Linear Gradient (`LinearGradientMask`, toạ độ chuẩn hoá + smoothstep, có test).
- [x] **6.3** Radial Gradient (`RadialMask`, elip + feather + invert, có test).
- [x] **6.4** Brush (`BrushMask`, chuỗi chấm toạ độ chuẩn hoá + Radius/Hardness, hợp max, qua MaskedOp + test).
      UI vẽ tay trên canvas (`CenterPreview.Brush`: bắt nét kéo chuột, chấm phản hồi tức thì, gắn vào brush mask đang chọn).
- [x] **6.5** Range mask Luminance (`LuminanceRangeMask`) + Range theo màu (`ColorRangeMask`: hue±range +
      ngưỡng sat, mép mượt, qua MaskedOp + test).
- [~] **6.6** AI mask Subject (`OnnxSegmenter` U²-Net -> `RasterMask` -> MaskedOp, cache theo path+mtime) +
      **Sky mask heuristic** (`SkyMask`, không cần AI) — nút "AI Subject" + "+ Sky" trong Local Adjustments.
      Background = Subject invert. Sky bằng model phân lớp riêng CHƯA. (AI Subject cần model ONNX.)
- [x] **6.7** Mỗi local mask có full bộ slider Light/Color như global (`LocalMask` + DevelopPanel.Masks:
      Exposure/Contrast/Highlights/Shadows/Whites/Blacks/Temp/Tint/Sat/Vibrance/Clarity/Sharpen; mỗi mask sinh
      MaskedOp riêng, gom nhóm theo maskId khi load).

---

## 7. RAW & ĐỊNH DẠNG (Darktable lõi là RAW)

- [~] **7.1** RAW: `RawPreviewExtractor` + `RawPreviewDecoder` mở/xem/develop RAW qua JPEG preview nhúng
      (CR2/CR3/NEF/ARW/DNG/RAF/RW2/ORF/PEF/SRW...), đăng ký vào `ImageDecoderRegistry`, có test. Demosaic
      sensor thật (LibRaw native plugin) CHƯA — registry đã sẵn để plugin đè decoder.
- [ ] **7.2** Demosaic + WB as-shot từ metadata RAW (cần LibRaw — preview hiện dùng JPEG máy tạo).
      **Scaffold LibRaw ĐÃ XONG** (`LibRawNative`/`LibRawImageConverter`/`LibRawDecoder`, gated, có test);
      cần bundle libraw.dll + verify file RAW thật để bật demosaic thật.
- [~] **7.3** Camera/input color profile (DCP/ICC) -> chuyển về working space linear.
      **Đọc ICC nhúng ĐÃ XONG** (`IccProfileReader`: parse desc v2/v4, đoán gamut, tự gợi ý Input Profile).
      **Parse ICC colorant matrix ĐÃ XONG** (`TryReadRgbToXyzD65`: rXYZ/gXYZ/bXYZ s15Fixed16 -> ma trận thật,
      Bradford D50→D65; `ColorSpaces.MatchSpace` nhận diện gamut theo ma trận khi tên không khớp).
      **Ghi output ICC ĐÃ XONG** (`IccProfileWriter`: dựng ICC v2 matrix profile để nhúng khi export — xem 9.8).
      DCP camera profile đầy đủ CHƯA (cần dữ liệu profile máy).
- [~] **7.4** 16-bit PNG/TIFF decode (`StandardImageDecoder` + IsHighBitDepth) + xuất 16-bit PNG + export TIFF. DNG CHƯA (cần RAW).
- [ ] **7.5** Đọc/áp camera profile & picture style.

---

## 8. CATALOG / THƯ VIỆN (Lightroom Library module)

- [x] **8.1** Đồng bộ/khẳng định CatalogService (SQLite) đã hoạt động đầy đủ với UI hiện tại.
      Collection (tạo/đổi tên/xoá + add/remove ảnh, bỏ qua ảnh chưa import + trùng) đã có test xác nhận.
- [x] **8.2** Keywords / tags phân cấp + tích hợp VisionTagger (`KeywordHelper` phân cấp + VisionTagger
      "Lưu vào Keywords" + search workspace khớp keyword). + test. **D6.4:** trình sửa Keywords trong InfoPanel
      (chip thêm/gỡ, nhập phân cấp, gợi ý từ điển + recently-used qua `AppSettings.TagDictionary/RecentTags`).
- [x] **8.3** Smart Collections (lọc theo rule: rating, keyword, EXIF, ngày...) — `SmartCollection` + dialog. + test.
- [x] **8.4** Tìm kiếm nâng cao (metadata, keyword, camera, lens, ISO...) — `CatalogQuery` + `SearchAdvanced`. + test.
- [x] **8.5** Map / GPS view — `GpsHelper` (DMS->decimal + validate + map URL), `ExifReader.TryReadGps`,
      cột GPS trong catalog, InfoPanel hiện toạ độ + nút "🗺 Bản đồ" mở Google Maps. + test.
- [x] **8.6** Compare/Survey view: Cull 2x2 + **side-by-side before/after (Y) + zoom đồng bộ 2 khung** (mousewheel).
- [x] **8.7** Stacking ảnh (`ImageStacker`: StackByTime burst/bracket + StackByBaseName, có test) +
      UI Grid: nút Stack gom theo thời gian -> cover + badge số lượng, mở lại nguyên trạng.

---

## 9. PRESET / STYLE / XUẤT

- [x] **9.1** Develop Presets: lưu/áp qua StyleService (combo + nút Lưu trong DevelopPanel).
- [x] **9.2** Copy/Paste settings (`DevelopClipboard`) + Sync selection + phím tắt + context menu.
- [~] **9.3** Import preset XMP của Lightroom (`LightroomXmpImporter`: crs:* -> EditOps -> Style, có UI + test).
      **+ Tone Curve tổng + per-channel R/G/B (ToneCurvePV2012[Red/Green/Blue] rdf:Seq -> ToneCurveOp,
      chuẩn hoá 0..255→0..1, bỏ identity) + Split Toning (hue/sat/balance) + HSL/Color Mixer 8 dải +
      Color Grading 3-way + Texture + Grain.**
      Style Darktable (.dtstyle) CHƯA (format nhị phân hex theo version module — decode dễ sai).
- [x] **9.4** Export: watermark chữ, resize %/cạnh dài, TIFF, pattern đổi tên, **Sharpen-for-output**
      (None/Screen/Print Low/Print High qua GaussianSharpen, áp sau resize trong ExportBatchAdapter + UI ComboBox).
      **Giữ EXIF gốc khi export** (`ExifWriter.PreserveExif`/`SanitizeProfile`: copy camera/lens/ISO/ngày/GPS,
      reset orientation về Normal, bỏ kích thước cũ; checkbox "Giữ EXIF gốc" mặc định bật). + test.
      **Không ghi đè im lặng**: `FileNameTokenizer.EnsureUniquePath` thêm hậu tố " (1)"... khi trùng tên đích. + test.
- [x] **9.5** XMP sidecar (`XmpSidecar`, namespace imgtool:, tùy chọn writeXmp khi export + test).
- [x] **9.6** Nén sâu (Squoosh-style) — `EncoderFactory` map params → encoder ImageSharp thật cho từng định dạng:
      PNG (CompressionLevel 0–9 + palette PNG-8 Wu-quantizer 2–256 màu + interlace), JPEG (chroma subsample
      4:2:0/4:2:2/4:4:4 + progressive), WebP (lossy/lossless/near-lossless + effort 0–6), TIFF (LZW/Deflate/
      PackBits/None + horizontal predictor). **Dung lượng mục tiêu** (`TargetSizeEncoder`: binary-search quality
      để file ≤ target KB cho jpg/webp). **Strip metadata** (SkipMetadata) cho web. Nối ExportBatchAdapter +
      Expander "Nén nâng cao" trong ExportPanel (hiện theo định dạng), persist trong ExportPreset, ước lượng
      dung lượng theo tuỳ chọn (`EstimateBytesWithOptions`). + 17 test.
- [x] **9.7** Watermark vô hình (blind, lấy cảm hứng blind_watermark) — `BlindWatermark` (DCT 8x8 + QIM
      nhúng chuỗi bit vào 2 hệ số tần trung của kênh luminance, lặp toàn ảnh + bộ phiếu đa số khi giải mã —
      bền với nhiễu/JPEG nhẹ). Embed/Extract round-trip, gần như không nhìn thấy. **+ Bản resize-resilient**
      (`EmbedResilient`/`ExtractResilient`: chuẩn hoá luminance về lưới canonical 256px trước embed/extract +
      QIM step lớn -> sống sót khi ảnh xuất bị phóng/thu đều). Nối ExportBatchAdapter (dùng resilient, nhúng
      ở độ phân giải xuất, giữ EXIF/ICC) + ô nhập trong "Nén nâng cao". + 11 test.
- [x] **9.8** Output color profile khi export (D2.2) — `IccProfileWriter` dựng ICC v2 RGB matrix profile
      (header + desc/wtpt/rXYZ-gXYZ-bXYZ colorant D50 qua Bradford + TRC gamma + cprt) cho sRGB/AdobeRGB/
      Rec2020/DisplayP3; round-trip qua `IccProfileReader` + sống sót PNG/JPG save-load. Nối ExportBatchAdapter
      (nhúng `image.Metadata.IccProfile`) + ComboBox "Output Color Profile" trong ExportPanel + persist preset. + 10 test.

---

## 10. HIỆU SUẤT (perf)

- [x] **10.1** Preview proxy: render slider trên ảnh thu nhỏ (2048px), full-res chỉ khi export (`DevelopRenderer`).
- [x] **10.2** `WriteableBitmap` (BGRA32, encode sRGB tại chỗ) thay re-encode PNG cho preview Develop. (ColorLab cũ vẫn re-encode — sẽ xử lý ở 3.7.)
- [x] **10.3** Cache `LinearImage` proxy theo path trong `DevelopRenderer` (không decode lại khi chỉ đổi op).
- [x] **10.4** Debounce 40ms + hủy job render cũ khi kéo slider (`CancellationToken`).
- [x] **10.5** SIMD (`Vector<float>`) cho WB+Exposure (per-channel multiply) + test khớp scalar.
- [x] **10.6** Cache theo tầng: `CachedEditPipeline` lưu snapshot sau từng op, replay chỉ từ op đầu tiên bị
      đổi (longest-common-prefix theo chữ ký op). Bộ nhớ giới hạn `MaxCheckpoints`. Nối vào DevelopRenderer
      cho preview; 8 test khẳng định kết quả trùng khít `EditPipeline`.
- [~] **10.7** GPU compute cho Develop (ComputeSharp/DirectML) - dài hạn. Đã có `PipelineProfiler` đo bottleneck
      từng op làm cơ sở quyết định; rewrite GPU chưa làm (rủi ro cao, cần verify shader trên máy thật).
- [x] **10.8** ArrayPool cho buffer blur trung gian (GaussianBlur) — giảm GC khi kéo slider.
- [x] **10.9** Thumbnail: xác nhận decode bằng TargetSize hint (đã có) + cache đĩa hoạt động tốt.
      `ThumbnailService.ComposeCacheKey` (path+mtime+size+target) có test xác nhận invalidate khi file đổi.
- [ ] **10.10** Plugin load: cân nhắc AssemblyLoadContext riêng (hiện load chung default ALC, có hack BAML).
- [x] **10.11** LUT 4096-mức encode sRGB nhanh (EncodeByteFast) cho preview triệu pixel + test.

---

## 11. UX / UI

- [x] **11.1** Develop module: tab Develop nhóm thu gọn được (Expander): WB/Tone/ParametricCurve/Presence/HSL/SplitToning/Detail/Effects/Geometry.
- [x] **11.2** Slider: double-click reset + ô nhập số trực tiếp (Enter/blur) + hiển thị giá trị.
- [x] **11.3** Histogram trực quan + cảnh báo clip (live trong DevelopPanel: RGB/Luma toggle, marker
      shadow/highlight + %). Kéo trực tiếp trên histogram để chỉnh tone ĐÃ XONG (13.10). **+ Waveform /
      RGB-Parade scope (`WaveformData` + nút "Wave", vẽ WriteableBitmap log-scale). + test.**
- [x] **11.4** Before/After: splitter (cũ) + giữ phím `\` xem ảnh gốc + **side-by-side (phím Y)** 2 khung.
- [x] **11.5** Zoom/Pan loupe: wheel-zoom quanh con trỏ, Z toggle fit/100%, +/-, right-drag pan, **Space + kéo trái để pan** (kiểu Photoshop).
- [x] **11.6** Phím tắt kiểu LR: rating/flag/label + Ctrl+Shift+C/V + Ctrl+Z/Y + **D/M module switch** + R crop + J clip + Y compare + **O đổi guide crop** + **← → điều hướng ảnh trước/kế** + **Esc huỷ crop / Enter áp crop** + **F1 hoặc ? mở bảng phím tắt (cheat-sheet overlay)**.
- [x] **11.7** Hiển thị tiến trình render/AI rõ ràng ở status bar (ReportProgress hiện ghi vào txtMeta - TODO trong code).
- [x] **11.8** Tooltip cho nút Develop (Copy/Paste/Auto/Reset) + trạng thái rỗng "Chọn ảnh để bắt đầu".
      **+ Tooltip mô tả cho mọi slider Develop (qua AddSlider) + empty-state hint cho History/Style/Batch
      (InverseBoolToVis converter) + bảng phím tắt F1/?.** Onboarding đầy đủ CHƯA.
- [x] **11.9** Theme: `ThemeManager` + `LightTheme.xaml` + nút đổi Sáng/Tối + lưu setting (áp lúc khởi động).
      **Migrate toàn bộ panel chính sang DynamicResource** (thêm key SuccessBrush/DangerBrush/SelectionBrush;
      MainWindow/Filmstrip/ToolsWindow/HistoryPanel/StylePanel/ExportPanel/CollectionsPanel/BatchQueuePanel/
      WorkspaceBrowser/InfoPanel/ImportDialog/CenterPreview + DevelopPanel & partials qua SetResourceReference).
      Data-viz (histogram/curve/wheel/crop+liquify overlay) giữ literal cố ý vì không phải chrome.
- [~] **11.10** Responsive panel: kéo rộng/hẹp + **nhớ bề rộng panel trái/phải** (lưu `LeftPanelWidth`/
      `RightPanelWidth` vào AppSettings, khôi phục lúc khởi động); pop-out tools (đã có) ổn định đa màn hình.
- [x] **11.11** Undo/redo có nhãn rõ ("Hoàn tác: Exposure" qua `OpDisplayNames`) + history panel nhãn thân thiện.
      **Thumbnail từng bước ĐÃ XONG** (`DevelopRenderer.RenderThumbnailAsync`: render mini-preview 44px mỗi mốc
      history off-UI, gán dần qua INotifyPropertyChanged — xem trực quan từng bước edit). **Named Snapshots ĐÃ
      XONG**: lưu/áp/xoá mốc edit có tên (mục SNAPSHOTS trong HistoryPanel, persist sidecar) — đường tắt tới
      virtual copies (D6.3).
- [x] **11.12** Badge "đã chỉnh sửa" (✎) trong grid + filmstrip, cập nhật theo history.
- [x] **11.13** Toast không chặn ở đáy cửa sổ (báo batch/export xong/lỗi, tự ẩn 3s).
- [x] **11.14** Fix nhảy tab panel phải: header TabControl luôn 1 hàng, cuộn ngang (template tùy biến).
- [x] **11.15** Context menu chuột phải trên thumbnail (Browser/Filmstrip/Grid): Copy/Paste/Reset settings,
      Rating, Color Label, Flag, Show in Explorer, Copy File Path. Áp cho cả selection.
- [x] **11.16** Copy/Paste Develop settings nhanh (`DevelopClipboard`): nút + phím tắt, paste nhiều ảnh.

---

## 12. KỸ THUẬT / DỌN DẸP

- [x] **12.1** Dọn sạch toàn bộ 24 warning nullable trong plugin (build 0 warning).
- [x] **12.2** Gỡ `ImageTool.Worker.Upscaler` (out-of-process .NET cũ) — đã xác nhận in-process `OnnxUpscaler`
      thay thế hoàn toàn, không project/code nào tham chiếu; xoá khỏi solution + git.
- [x] **12.3** Pin SDK ổn định qua `global.json` (9.0.312, latestFeature) — hết cảnh báo NETSDK1057.
- [x] **12.4** 178 unit test cho `ImageTool` (op linear, replay, mask, geometry + perspective, color unify,
      tone-curve math, cached pipeline, histogram, B&W/Invert/Auto WB, crop aspect, catalog query/smart collection,
      keyword helper, filename tokenizer, op display names, SIMD, decode/encode, XMP).
- [x] **12.5** Tài liệu `ImageTool.Imaging/WRITING_OPS.md` — hướng dẫn viết op mới.

---

## 13. TÍNH NĂNG MỚI & UX BỔ SUNG (phiên 2026-05-29 d)

> Đề xuất + thực hiện thêm để hoàn thiện phần mềm. Tất cả op có test, build 0 warning, commit từng phần.

- [x] **13.1** Black & White conversion (`BlackWhiteOp`): channel mixer mono (R/G/B weights) + nhuộm
      (ToneHue/ToneStrength). UI group "Black & White" trong Develop. + test. **+ Preset filter màu cổ điển
      (Red/Orange/Yellow/Green/Blue) mô phỏng kính lọc khi chụp phim B&W.**
- [x] **13.2** Auto White Balance (`AutoWhiteBalance`: gray-world + white-patch) → áp qua `ChannelGainOp`.
      Nút "Auto WB" trong nhóm White Balance. + test.
- [x] **13.3** Negative / Invert (`InvertOp`, đảo trong sRGB cho workflow scan phim). Toggle trong Effects. + test.
      **Film Negative (negadoctor):** `FilmNegativeOp` khử film base màu cam + đảo trong miền mật độ
      (per-channel base/gamma/exposure, SampleBase lấy mẫu mép phim), nhóm "Film Negative" + eyedropper
      "Pick film base" (click mép phim trống) + test.
- [x] **13.4** Crop aspect-ratio presets (`CropAspect`: 1:1/4:3/3:2/16:9/5:4/dọc + Original). ComboBox trong
      thanh Crop, căn giữa khung. + test.
- [x] **13.5** Export presets (`ExportPreset` trong AppSettings): lưu/gọi/xoá toàn bộ thiết lập Export (combo + nút).
- [x] **13.6** Filename token engine (`FileNameTokenizer`): {name}/{ext}/{n:000}/{date[:fmt]}/{time}/{w}/{h}/{parent},
      chống trùng theo lô. Nối vào Export pattern. + test. (Hạ tầng batch rename sẵn sàng.)
- [x] **13.8** Histogram RGB/Luma channel toggle trong DevelopPanel.
- [x] **13.9** Clipping overlay trên preview (phím **J**): đỏ = highlight cháy, xanh = shadow crushed,
      đồng bộ zoom/pan, cập nhật theo render.
- [x] **13.11** Nối B&W / Invert / Auto WB / crop-ratio vào Develop + Crop UI.
- [x] **13.7** Batch Rename UI (`BatchRenameDialog` + `BatchRenamer`): dialog xem trước live, đổi tên an toàn
      2 pha (xử lý hoán đổi/đụng tên), mở từ context menu thumbnail. + test.
- [x] **13.10** Histogram tương tác kéo trực tiếp để chỉnh tone — kéo ngang trên histogram chỉnh slider Basic
      theo vùng tone (Blacks/Shadows/Exposure/Highlights/Whites trái→phải), commit debounce như slider.

---

## 14. SÁP NHẬP DARKTABLE (plan chi tiết)

> Xem `DARKTABLE_PLAN.md` — đối chiếu module Darktable với op đã có; phần còn thiếu chia 6 phase
> (D1 scene-referred tone, D2 color science, D3 detail/correction, D4 mask/local nâng cao,
> D5 RAW thật/LibRaw, D6 lighttable).
>
> **ĐỢT 1 HOÀN TẤT (2026-05-30):**
> - **D4.5** Blend modes (12 chế độ) + opacity cho mỗi local op (`BlendModes`).
> - **D1.1** `SigmoidOp` — sigmoid tone mapping mượt vùng rực.
> - **D1.2** `FilmicRgbOp` đầy đủ — white/black relative exposure, latitude, contrast, sat highlight.
> - **D1.3** `ToneEqualizerOp` — chỉnh sáng theo dải vùng (zone-based).
> - **D2.1** `ColorBalanceRgbOp` 4-way (lift/gamma/gain + offset) + global chroma/contrast.
> - **D2.3** `VelviaOp`, **D2.4** `ColorContrastOp` (Lab a*/b*), **D2.5** `RgbLevelsOp`.
> - **D4.1** `ParametricMask` — chọn vùng theo 6 kênh (L/C/h/R/G/B) band-pass giao nhau, hue wrap, invert;
>   nối MaskedOp + LocalMask + DevelopPanel ("+ Param"). + test.
>
> **ĐỢT 2 HOÀN TẤT (2026-05-30):** D2.4 Color Contrast / D2.5 Levels / D2.3 Velvia; D3.3 Hot Pixel +
> D3.4 CA Correct + D3.2 Chroma denoise edge-aware (op + UI Detail + test); D6.1 Selective paste module
> (`DevelopModules` + context menu "chọn module"); D6.2 Style append (`ApplyToImageMerged` + checkbox Append).
> **ĐỢT 3 (đang làm):** D3.1 Diffuse-or-sharpen ✅ xong (`DiffuseOp` PDE Perona–Malik: sharpen bám cạnh /
> denoise giữ cạnh); D2.2 color management 1 phần ✅ (`ColorSpaces` matrix + `InputProfileOp`
> sRGB/AdobeRGB/Rec2020/P3, UI "Color Management"). Còn lại (nặng/cần native): parse ICC nhúng + output
> profile, D5.x RAW thật (LibRaw native), D3.5 liquify, D4.4 instance UI, D6.3 virtual copies.
>
> **PHASE D1 HOÀN TẤT:** D1.1 Sigmoid + D1.2 Filmic RGB + D1.3 Tone Equalizer + D1.4 ToneCurve preserve-hue.
>
> **NGOÀI PLAN (parity bổ sung):** `GlowOp` (soften/Orton glow: blur + screen blend + bright-pass) trong Effects.
>
> **DYNAMIC RANGE & FOCUS (2026-05-30):**
> - **Exposure Fusion (Mertens)** — `ExposureFusion` + `Pyramid` (Laplacian/Gaussian): ghép chùm bracket
>   tăng dynamic range THẬT (3 trọng số contrast/saturation/well-exposedness, multi-scale blend).
> - **Focus measure** — `FocusMeasure` (variance-of-Laplacian + Tenengrad + focus map): phát hiện vùng nét /
>   ảnh out nét (KHÔNG sửa được ảnh đã mờ — thông tin đã mất; chỉ deblur nhẹ qua DiffuseOp).
> - **Focus Stacking** — `FocusStack`: ghép nhiều ảnh lấy nét khác khoảng cách thành 1 ảnh nét toàn bộ
>   (softmax theo focus map từng pixel).
> - **MergeService** + context menu "Merge..." (Merge to HDR / Focus Stack) trên selection, xuất PNG 16-bit.
>   Đều có test (fusion/focus/stack + decode→merge→save).

---

## 15. BACKLOG — CẦN NATIVE / GPU / MODEL / DỮ LIỆU PHẦN CỨNG (làm sau, verify trên máy thật)

> Các mục này KHÔNG test trọn vẹn được bằng unit test thuần vì phụ thuộc phần cứng/binary/model.
> Phần managed (engine + pipeline + math) có thể đã/đang làm; phần đánh dấu cần verify ngoài đời thật.

- [ ] **15.1 GPU compute cho pipeline** (10.7) — port các op nặng (Gaussian blur, Clarity, Diffuse, Dehaze,
      LocalToneMap) sang ComputeSharp/DirectML. Đòn bẩy perf lớn nhất còn lại. `PipelineProfiler` đã có để
      chọn op đáng port. **Cần verify shader trên GPU thật** (NVIDIA/AMD/Intel) + so khớp kết quả CPU.
- [ ] **15.2 LibRaw demosaic THẬT** (D5.1/D5.2) — scaffold `LibRawDecoder`/`LibRawNative`/`LibRawImageConverter`
      đã xong (gated, có test). **Cần bundle `libraw.dll` x64 + DLL phụ thuộc vào `native/` + verify P/Invoke
      + chất lượng demosaic trên file RAW thật** (CR2/CR3/NEF/ARW/DNG...). Chọn thuật toán demosaic (PPG/AMaZE/RCD)
      qua param: CHƯA.
- [ ] **15.3 DCP/camera profile đầy đủ** (D5.4/7.3/7.5) — `InputProfileOp.SourceMatrix` (nền tảng ma trận) ✅
      test được. CÒN LẠI: **parse binary DCP/DNG ColorMatrix + ForwardMatrix + HSV lookup table** (cần dữ liệu
      profile máy ảnh thật) + Bradford về working. Parse format nhị phân theo từng máy -> verify trên RAW thật.
- [ ] **15.4 Soft-proof với ICC máy in/màn hình THẬT** (#1 mở rộng) — engine `GamutMapOp`/`GamutCheck` ✅ nhận
      ma trận tuỳ chỉnh. CÒN LẠI: **đọc gamut từ ICC máy in/màn hình thật** (LUT-based profile, perceptual/
      relative-colorimetric intent — không chỉ matrix profile) + overlay cảnh báo out-of-gamut trên canvas.
      Cần file ICC thiết bị thật để verify.
- [ ] **15.5 AI segmentation đa lớp** (6.6 mở rộng) — Sky hiện heuristic; Subject cần ONNX. **Cần model ONNX
      (U²-Net / segmentation đa lớp người/da/tóc/nền) + GPU** để verify inference. Pipeline mask + RasterMask
      đã sẵn sàng nhận kết quả.
- [ ] **15.6 Tethering** — chụp tether qua USB (libgphoto2/Canon/Nikon SDK). Cần thiết bị máy ảnh thật + SDK
      hãng -> không test bằng unit test.
- [ ] **15.7 Print ra máy in thật** (#11 mở rộng) — hiện `PrintModule` xuất file raster sẵn-sàng-in (test được).
      CÒN LẠI: gửi thẳng `System.Drawing.Printing`/`System.Printing` tới máy in + chọn khay/giấy. Cần máy in thật.
- [ ] **15.8 Multiple instances cho op GLOBAL** (D4.4) — pipeline đã hỗ trợ nhiều MaskedOp; còn UI cho 2+
      instance op global (vd 2 Tone Curve khác nhau). Thuần UI, sẽ làm khi cần (không chặn).
- [ ] **15.9 Virtual copies đầy đủ** (D6.3) — đã có Named Snapshots; còn model history per-version + hiển thị
      nhiều bản song song trong grid + selection-by-copy-id. Thuần managed nhưng động vào nhiều tầng, tách riêng.

---

## Ghi chú ưu tiên

1. **Phần 1 (Nền tảng) là blocker tuyệt đối** - làm trước. Không có nó, Phần 2-6 vô nghĩa.
2. Sau nền tảng, thứ tự giá trị cao: Phần 2 (Tone) -> 3 (Color/HSL) -> 6 (Local mask) -> 4 (Detail).
3. RAW (Phần 7) là tính năng lớn, làm khi pipeline đã ổn định.
4. Perf (Phần 10) làm song song khi nối pipeline (10.1-10.4 nằm ngay trong Phần 1).

