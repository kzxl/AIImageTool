# Darktable Feature-Merge Plan

> Plan sáp nhập tính năng Darktable còn thiếu. Đối chiếu với những gì ĐÃ có để tránh trùng.
> Cập nhật: 2026-05-30.

## A. ĐÃ CÓ TƯƠNG ĐƯƠNG (không cần làm lại)

Darktable module -> op của ta đã làm:
- exposure / basic adjustments -> `DevelopBasicOp` (exposure/contrast/highlights/shadows/whites/blacks/vibrance/sat)
- tone curve -> `ToneCurveOp` + UI kéo điểm; tone equalizer (1 phần) -> `ParametricCurveOp`
- color balance rgb (3-way) -> `ColorGradingOp` + color wheel
- color calibration (channel) -> `ChannelMixerOp`; white balance -> `WhiteBalanceKelvinOp` + Auto WB + eyedropper
- color zones (HSL) -> `HslMixerOp` (8 dải); color mapping/unify -> `ColorUnifyOp`; selective -> `SelectiveColorOp`
- filmic rgb -> `FilmicRgbOp` (đầy đủ: white/black relative, latitude, contrast); sigmoid -> `SigmoidOp`
- haze removal -> `DehazeOp`; local contrast/clarity -> `ClarityOp`; sharpen -> `SharpenOp`; texture -> `TextureOp`
- diffuse or sharpen -> `DiffuseOp` (PDE Perona–Malik: sharpen bám cạnh / denoise giữ cạnh)
- denoise (profiled, 1 phần) -> `ColorNoiseReductionOp` + `LumaNoiseReductionOp` + `ChromaDenoiseOp` (cross-bilateral) + AI denoise (`AiDenoiseOp`)
- chromatic aberration/defringe -> `DefringeOp` (viền) + `CaCorrectOp` (lateral CA radial); vignette -> `VignetteOp`; grain -> `GrainOp`
- soften/glow (Orton) -> `GlowOp` (blur + screen blend + bright-pass threshold)
- hot/dead pixel -> `HotPixelOp`
- lens correction (thủ công) -> `LensCorrectionOp` (distortion k1/k2 + vignette)
- crop / rotate / perspective (ashift) -> `CropOp` + `OrientationOp` + `PerspectiveOp`
- lut 3D -> `LutCubeOp`; monochrome -> `BlackWhiteOp`; invert -> `InvertOp`
- retouch (1 phần: heal/clone) -> `HealingOp`; drawn+parametric mask -> `MaskedOp` + 7 loại mask
  (gradient/radial/brush/lum-range/color-range/sky/raster) + `ParametricMask` đa kênh (L/C/h/R/G/B) + AI subject/sky
- raw (preview) -> `RawPreviewDecoder`; export, presets, history, snapshots(before/after) — có
- input/working color space (matrix, D2.2 1 phần) -> `ColorSpaces` + `InputProfileOp` (sRGB/AdobeRGB/Rec2020/P3)
- profiled denoise/upscale AI, dominant color, color harmony — có (vượt Darktable)

## B. PHASE D1 — Scene-referred tone (lõi Darktable hiện đại), test được

- [x] **D1.1** `SigmoidOp` — tone mapping sigmoid (display transform mượt, ít vỡ màu hơn filmic ở vùng rực).
- [x] **D1.2** `FilmicRgbOp` đầy đủ — white/black relative exposure, latitude, contrast, độ bão hoà vùng sáng
      (nâng cấp `FilmicOp` hiện tại vốn đơn giản).
- [x] **D1.3** `ToneEqualizerOp` — chỉnh sáng theo 8-9 dải vùng (zone) bằng mask guided, kiểu Ansel/Darktable.
- [ ] **D1.4** `RgbCurveOp` per-channel nâng cao + chế độ "preserve hue" (đã có ToneCurve, bổ sung chế độ).

## C. PHASE D2 — Color science nâng cao, test được

- [x] **D2.1** `ColorBalanceRgbOp` mở rộng — 4-way (lift/gamma/gain + offset) + global chroma/contrast/brilliance,
      perceptual (nâng `ColorGradingOp`).
- [~] **D2.2** Working color space + input/output profile: `ColorSpaces` (ma trận RGB↔XYZ D65 cho
      sRGB/AdobeRGB/Rec2020/DisplayP3 + invert/mul 3x3) + `InputProfileOp` quy ảnh nguồn gamut rộng về
      working linear sRGB bằng ma trận 3x3, nối UI "Color Management" + test. Đọc file ICC nhúng (managed
      parser) + output profile khi export CHƯA (cần parse ICC).
- [x] **D2.3** `VelviaOp` / saturation thông minh theo độ rực + luminance (giống module velvia).
- [x] **D2.4** `ColorContrastOp` — chỉnh tương phản trục a*/b* (green-magenta, blue-yellow) trong Lab.
- [x] **D2.5** `RgbLevelsOp` — levels (black/gray/white point per-channel + auto).

## D. PHASE D3 — Detail & correction, test được

- [x] **D3.1** `DiffuseOp` (diffuse or sharpen) — khuếch tán dẫn hướng Perona–Malik trên luminance:
      Amount dương = sharpen bám cạnh (không khuếch đại nhiễu), âm = denoise/làm mịn giữ cạnh; Iterations
      + EdgeSensitivity, vòng lặp theo scale. Nối UI Detail + DevelopModules + test.
- [x] **D3.2** `RawDenoiseOp` / chroma denoise nâng — `ChromaDenoiseOp` cross-bilateral (guide luminance,
      giữ cạnh, mượt chroma mạnh hơn ColorNR), bán kính theo scale, EdgeSensitivity. Nối UI Detail + test.
- [x] **D3.3** `HotPixelOp` — khử điểm chết/nóng (so 4 lân cận theo ngưỡng -> thay trung bình, có test).
- [x] **D3.4** `CaCorrectOp` — khử quang sai màu trục (lateral CA) theo co/giãn radial kênh R/B quanh tâm
      (bilinear, có test). Khác defringe (viền cục bộ).
- [ ] **D3.5** `LiquifyOp` (cơ bản) — kéo/đẩy điểm (warp) — phức tạp, để cuối.

## E. PHASE D4 — Mask & local nâng cao, test được

- [x] **D4.1** Parametric mask đầy đủ — chọn vùng theo nhiều kênh (L, C, h, R, G, B) với upper/lower + feather,
      kiểu Darktable parametric masking (`ParametricMask`: 6 kênh band-pass giao nhau, hue wrap, invert; nối
      MaskedOp + LocalMask + DevelopPanel "+ Param"; có test).
- [ ] **D4.2** Mask combine ops — union/intersect/difference giữa nhiều mask trên 1 instance.
- [ ] **D4.3** Path/Ellipse/Gradient mask có nhiều node + feather riêng (nâng Radial/Gradient hiện có).
- [ ] **D4.4** Multiple instances 1 module (vd 2 lần exposure khác mask) — pipeline đã hỗ trợ, cần UI quản lý instance.
- [x] **D4.5** Blend modes (normal/multiply/screen/overlay/...) + opacity cho mỗi op (Darktable "blending").

## F. PHASE D5 — RAW thật (cần native), tách riêng

- [ ] **D5.1** Plugin LibRaw (P/Invoke) đăng ký đè `ImageDecoderRegistry` -> demosaic sensor 12-14 bit thật.
- [ ] **D5.2** Demosaic chọn được (PPG/AMaZE/RCD) + WB as-shot từ metadata RAW.
- [ ] **D5.3** Highlight reconstruction (phục hồi vùng cháy từ kênh chưa bão hoà).
- [ ] **D5.4** Input color profile (DCP/camera matrix) -> working space.
- [ ] **D5.5** Hỗ trợ đa định dạng RAW rộng (rawspeed-like) — phụ thuộc LibRaw.

## G. PHASE D6 — Lighttable/quản lý kiểu Darktable

- [x] **D6.1** History stack có thể copy/paste TỪNG module giữa ảnh (selective paste) — `DevelopModules`
      (gom OpType -> 15 module + thứ tự pipeline chuẩn) + `DevelopClipboard.PasteModulesTo` + context menu
      "Paste Settings (chọn module)" checkable. Merge giữ module không chọn, sắp xếp lại canonical. + test.
- [x] **D6.2** Styles có thể append (không thay thế) + chọn module khi áp — `DevelopModules.ApplyStyle`
      (append giữ edit hiện có, chỉ thay/thêm module style; replace thay toàn bộ) + `StyleService.ApplyToImageMerged`
      + checkbox "Append" trong StylePanel + StyleBatchAdapter mode append/replace. + test.
- [ ] **D6.3** Duplicate / virtual copies (nhiều phiên bản edit của 1 ảnh) — cần model history per-version.
- [ ] **D6.4** Tagging phân cấp + recently used + tag từ điển (đã có keyword phẳng phân cấp; bổ sung từ điển/tag tree UI).
- [ ] **D6.5** Culling nâng cao + đánh dấu reject hàng loạt (đã có cull/stack; thêm flow lọc reject).

## H. ƯU TIÊN & ƯỚC LƯỢNG

Thứ tự khuyến nghị (giá trị / công sức / rủi ro):

**Đợt 1 — đòn bẩy cao, test được, không cần native (làm trước):** ✅ HOÀN TẤT (2026-05-30)
1. ✅ D4.5 Blend modes + opacity (mỗi op) — nâng tầm toàn bộ local adjustment, dùng lại ngay cho mọi op.
2. ✅ D1.1 Sigmoid + D1.2 Filmic RGB đầy đủ — chất lượng tone "ăn tiền" nhất của Darktable.
3. ✅ D4.1 Parametric mask đa kênh — biến masking thành "đúng Darktable".
4. ✅ D2.1 Color Balance RGB 4-way mở rộng.
5. ✅ D1.3 Tone Equalizer (zone-based).

**Đợt 2 — bổ sung detail/correction:** ✅ HOÀN TẤT (2026-05-30)
6. ✅ D2.4 Color Contrast (Lab a/b), D2.5 Levels, D2.3 Velvia.
7. ✅ D3.3 Hot pixel, ✅ D3.4 CA correct, ✅ D3.2 chroma denoise nâng.
8. ✅ D6.1 selective paste module + ✅ D6.2 style append.

**Đợt 3 — nặng / cần native / phức tạp (cân nhắc):**
9. [~] D2.2 ICC color management — matrix gamut (sRGB/AdobeRGB/Rec2020/P3) ✅; parse ICC nhúng + output profile còn lại.
10. ✅ D3.1 Diffuse-or-sharpen (PDE); D6.3 virtual copies (còn lại).
11. **D5.x RAW thật qua LibRaw** — bước nhảy lớn nhất; cần bundle native + verify trên máy thật.
12. D3.5 Liquify, D4.4 instance UI.

## I. NGUYÊN TẮC THỰC HIỆN
- Mỗi op mới: `IEditOp` linear-light, thuần tham số, **có unit test**, đăng ký `EditOpRegistry`, nối DevelopPanel.
- Giữ build 0 warning; commit từng mục; smoke test sau mỗi nhóm UI.
- Việc cần model/native (D5, AI): code + pipeline + test logic ở đây, **inference/native verify trên máy thật**.
- Blend modes (D4.5) nên làm sớm vì nó là hạ tầng dùng lại cho mọi op sau.
