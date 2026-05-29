# ImageTool - TODO & Roadmap

> File theo dõi tiến độ bền vững. Mục tiêu: đạt feature-parity với Lightroom + Darktable,
> tối ưu hiệu suất, và cải thiện UX/UI. Cập nhật mỗi khi xong 1 mục (đổi `[ ]` -> `[x]`).
>
> Cập nhật lần cuối: 2026-05-29 (d) — thêm section 13 (B&W, Auto WB, Invert, crop-ratio, export presets,
> filename tokenizer, histogram channel toggle, clipping overlay). 178/178 test pass, build 0 warning.

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

**CẦN DEPENDENCY NGOÀI (chưa làm — yêu cầu thư viện/model bên thứ ba):**
- RAW decode (7.1-7.3): cần LibRaw native + color profile. Decoder point đã sẵn (`ImageDecoderRegistry`).
- AI mask Subject/Sky (6.6): cần model ONNX segmentation.
- Lens correction (5.3): cần database lensfun.
- Import XMP/.dtstyle của LR/Darktable (9.3): cần mapping crs:* phức tạp.

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

- [ ] **3.1** White Balance theo nhiệt độ Kelvin thực (as-shot, eyedropper, presets) thay gain đơn giản. (Hiện: gain temp/tint trong DevelopBasic.)
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

- [x] **4.1** Sharpening (`SharpenOp`, unsharp mask: Amount/Radius/Threshold, scale-aware). Detail/Masking nâng cao CHƯA.
- [x] **4.2** Luminance noise reduction (`LumaNoiseReductionOp`, blur kênh Y giữ chroma + Detail, có UI + test).
- [ ] **4.3** Tích hợp AI denoise/upscale sẵn có vào pipeline (Upscaler, FaceRestorer) như op cuối chuỗi.
- [x] **4.4** Defringe (`DefringeOp`, khử viền tím/lục theo hue, có UI + test).

---

## 5. TÍNH NĂNG DEVELOP - Geometry & Optics

- [x] **5.1** Crop & Straighten (`CropOp`: crop chữ nhật + xoay, có test). UI crop chữ nhật kéo tay
      (`CenterPreview.Crop`: overlay khung + 8 tay nắm + thirds + shade, đồng bộ 2 chiều với DevelopPanel,
      phím R bật/tắt, ảnh hiển thị chưa-cắt khi đang chỉnh) + Straighten slider.
- [x] **5.2** Rotate/Flip 90° (`OrientationOp`, IResizingOp, nút xoay/lật trong UI + test).
- [ ] **5.3** Lens Correction (distortion/vignette/CA) - đọc profile lensfun (Darktable dùng lensfun).
- [x] **5.4** Perspective / Upright (`PerspectiveOp`, homography 3x3: Vertical/Horizontal keystone + Rotate +
      Scale bù viền, inverse-map song tuyến, IResizingOp, có UI trong Geometry + test).
- [x] **5.5** Vignette (`VignetteOp`, post-crop: amount/midpoint/feather, smoothstep).
- [x] **5.6** Grain (`GrainOp` ĐÃ XONG: amount/size/roughness deterministic + test; UI có slider Grain).

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
- [ ] **6.6** AI mask: Subject / Sky / Background (tận dụng ONNX - có thể dùng model segment).
- [x] **6.7** Mỗi local mask có full bộ slider Light/Color như global (`LocalMask` + DevelopPanel.Masks:
      Exposure/Contrast/Highlights/Shadows/Whites/Blacks/Temp/Tint/Sat/Vibrance/Clarity/Sharpen; mỗi mask sinh
      MaskedOp riêng, gom nhóm theo maskId khi load).

---

## 7. RAW & ĐỊNH DẠNG (Darktable lõi là RAW)

- [ ] **7.1** RAW decoder plugin cắm vào `ImageDecoderRegistry` (LibRaw qua P/Invoke hoặc managed).
- [ ] **7.2** Demosaic + WB as-shot từ metadata RAW (đã chừa `DecodedImage.Metadata`).
- [ ] **7.3** Camera/input color profile (DCP/ICC) -> chuyển về working space linear.
- [~] **7.4** 16-bit PNG/TIFF decode (`StandardImageDecoder` + IsHighBitDepth) + xuất 16-bit PNG + export TIFF. DNG CHƯA (cần RAW).
- [ ] **7.5** Đọc/áp camera profile & picture style.

---

## 8. CATALOG / THƯ VIỆN (Lightroom Library module)

- [ ] **8.1** Đồng bộ/khẳng định CatalogService (SQLite) đã hoạt động đầy đủ với UI hiện tại.
- [ ] **8.2** Keywords / tags phân cấp + tích hợp VisionTagger (auto-keyword).
- [ ] **8.3** Smart Collections (lọc theo rule: rating, keyword, EXIF, ngày...).
- [ ] **8.4** Tìm kiếm nâng cao (metadata, keyword, camera, lens, ISO...).
- [ ] **8.5** Map / GPS view (nếu có GPS EXIF) - tùy chọn.
- [ ] **8.6** Compare/Survey view (đã có Cull 2x2, mở rộng zoom đồng bộ).
- [ ] **8.7** Stacking ảnh (gom nhóm bracket/burst).

---

## 9. PRESET / STYLE / XUẤT

- [x] **9.1** Develop Presets: lưu/áp qua StyleService (combo + nút Lưu trong DevelopPanel).
- [x] **9.2** Copy/Paste settings (`DevelopClipboard`) + Sync selection + phím tắt + context menu.
- [ ] **9.3** Import preset XMP của Lightroom (.xmp) và style Darktable (.dtstyle) - tùy chọn.
- [x] **9.4** Export: watermark chữ, resize %/cạnh dài, TIFF, pattern đổi tên, **Sharpen-for-output**
      (None/Screen/Print Low/Print High qua GaussianSharpen, áp sau resize trong ExportBatchAdapter + UI ComboBox).
- [x] **9.5** XMP sidecar (`XmpSidecar`, namespace imgtool:, tùy chọn writeXmp khi export + test).

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
- [ ] **10.7** Cân nhắc GPU compute cho Develop (ComputeSharp/DirectML) - tùy chọn dài hạn.
- [x] **10.8** ArrayPool cho buffer blur trung gian (GaussianBlur) — giảm GC khi kéo slider.
- [ ] **10.9** Thumbnail: xác nhận decode bằng TargetSize hint (đã có) + cache đĩa hoạt động tốt.
- [ ] **10.10** Plugin load: cân nhắc AssemblyLoadContext riêng (hiện load chung default ALC, có hack BAML).
- [x] **10.11** LUT 4096-mức encode sRGB nhanh (EncodeByteFast) cho preview triệu pixel + test.

---

## 11. UX / UI

- [x] **11.1** Develop module: tab Develop nhóm thu gọn được (Expander): WB/Tone/ParametricCurve/Presence/HSL/SplitToning/Detail/Effects/Geometry.
- [x] **11.2** Slider: double-click reset + ô nhập số trực tiếp (Enter/blur) + hiển thị giá trị.
- [ ] **11.3** Histogram tương tác (kéo trực tiếp trên histogram để chỉnh tone; cảnh báo clip highlight/shadow).
- [~] **11.4** Before/After: splitter (cũ) + giữ phím `\` xem ảnh gốc. Side-by-side CHƯA.
- [ ] **11.5** Zoom/Pan loupe mượt (fit/100%/zoom level), space để pan.
- [x] **11.6** Phím tắt kiểu LR: có rating/flag/label + **Ctrl+Shift+C/V copy-paste settings**, Ctrl+Z/Y. Thêm D/R/M CHƯA.
- [x] **11.7** Hiển thị tiến trình render/AI rõ ràng ở status bar (ReportProgress hiện ghi vào txtMeta - TODO trong code).
- [x] **11.8** Tooltip cho nút Develop (Copy/Paste/Auto/Reset) + trạng thái rỗng "Chọn ảnh để bắt đầu". Onboarding đầy đủ CHƯA.
- [ ] **11.9** Theme: rà soát DarkTheme cho nhất quán; cân nhắc light theme tùy chọn.
- [ ] **11.10** Responsive panel: kéo rộng/hẹp, nhớ layout; pop-out tools (đã có) ổn định đa màn hình.
- [ ] **11.11** Undo/redo có nhãn rõ ("Hoàn tác: Exposure"); history panel hiển thị thumbnail từng bước (tùy chọn).
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
      (ToneHue/ToneStrength). UI group "Black & White" trong Develop. + test.
- [x] **13.2** Auto White Balance (`AutoWhiteBalance`: gray-world + white-patch) → áp qua `ChannelGainOp`.
      Nút "Auto WB" trong nhóm White Balance. + test.
- [x] **13.3** Negative / Invert (`InvertOp`, đảo trong sRGB cho workflow scan phim). Toggle trong Effects. + test.
- [x] **13.4** Crop aspect-ratio presets (`CropAspect`: 1:1/4:3/3:2/16:9/5:4/dọc + Original). ComboBox trong
      thanh Crop, căn giữa khung. + test.
- [x] **13.5** Export presets (`ExportPreset` trong AppSettings): lưu/gọi/xoá toàn bộ thiết lập Export (combo + nút).
- [x] **13.6** Filename token engine (`FileNameTokenizer`): {name}/{ext}/{n:000}/{date[:fmt]}/{time}/{w}/{h}/{parent},
      chống trùng theo lô. Nối vào Export pattern. + test. (Hạ tầng batch rename sẵn sàng.)
- [x] **13.8** Histogram RGB/Luma channel toggle trong DevelopPanel.
- [x] **13.9** Clipping overlay trên preview (phím **J**): đỏ = highlight cháy, xanh = shadow crushed,
      đồng bộ zoom/pan, cập nhật theo render.
- [x] **13.11** Nối B&W / Invert / Auto WB / crop-ratio vào Develop + Crop UI.
- [ ] **13.7** Batch Rename UI (dialog đổi tên hàng loạt tại chỗ) — engine `FileNameTokenizer.ResolveBatch` đã sẵn,
      còn thiếu dialog + thao tác File.Move an toàn.
- [ ] **13.10** Histogram tương tác kéo trực tiếp để chỉnh tone (mới có hiển thị + clip warning).

---

## Ghi chú ưu tiên

1. **Phần 1 (Nền tảng) là blocker tuyệt đối** - làm trước. Không có nó, Phần 2-6 vô nghĩa.
2. Sau nền tảng, thứ tự giá trị cao: Phần 2 (Tone) -> 3 (Color/HSL) -> 6 (Local mask) -> 4 (Detail).
3. RAW (Phần 7) là tính năng lớn, làm khi pipeline đã ổn định.
4. Perf (Phần 10) làm song song khi nối pipeline (10.1-10.4 nằm ngay trong Phần 1).

