# Darktable Feature-Merge Plan

> Plan sáp nhập tính năng Darktable còn thiếu. Đối chiếu với những gì ĐÃ có để tránh trùng.
> Cập nhật: 2026-05-30.

## A. ĐÃ CÓ TƯƠNG ĐƯƠNG (không cần làm lại)

Darktable module -> op của ta đã làm:
- exposure / basic adjustments -> `DevelopBasicOp` (exposure/contrast/highlights/shadows/whites/blacks/vibrance/sat)
- tone curve -> `ToneCurveOp` + UI kéo điểm + chế độ preserve-hue (D1.4); tone equalizer -> `ToneEqualizerOp` + `ParametricCurveOp`
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
- crop / rotate / perspective (ashift) -> `CropOp` + `OrientationOp` + `PerspectiveOp`; liquify/warp -> `LiquifyOp` (handle đẩy/kéo)
- lut 3D -> `LutCubeOp`; monochrome -> `BlackWhiteOp`; invert -> `InvertOp`; film negative (negadoctor) -> `FilmNegativeOp`
- retouch (1 phần: heal/clone) -> `HealingOp`; drawn+parametric mask -> `MaskedOp` + 8 loại mask
  (gradient/radial/brush/polygon/lum-range/color-range/sky/raster) + `ParametricMask` đa kênh (L/C/h/R/G/B)
  + mask combine (intersect/union/subtract) + AI subject/sky
- raw (preview) -> `RawPreviewDecoder`; export, presets, history, snapshots(before/after) — có
- input/working color space (matrix, D2.2 1 phần) -> `ColorSpaces` + `InputProfileOp` (sRGB/AdobeRGB/Rec2020/P3)
- profiled denoise/upscale AI, dominant color, color harmony — có (vượt Darktable)

## B. PHASE D1 — Scene-referred tone (lõi Darktable hiện đại), test được

- [x] **D1.1** `SigmoidOp` — tone mapping sigmoid (display transform mượt, ít vỡ màu hơn filmic ở vùng rực).
- [x] **D1.2** `FilmicRgbOp` đầy đủ — white/black relative exposure, latitude, contrast, độ bão hoà vùng sáng
      (nâng cấp `FilmicOp` hiện tại vốn đơn giản).
- [x] **D1.3** `ToneEqualizerOp` — chỉnh sáng theo 8-9 dải vùng (zone) bằng mask guided, kiểu Ansel/Darktable.
- [x] **D1.4** `RgbCurveOp` per-channel nâng cao + chế độ "preserve hue" — `ToneCurveOp.PreserveHue`:
      master curve áp lên luminance rồi scale RGB theo tỉ lệ (giữ hue/sat), tránh dịch màu vùng rực;
      per-channel R/G/B vẫn áp sau. UI checkbox "Preserve hue" + test.

## C. PHASE D2 — Color science nâng cao, test được

- [x] **D2.1** `ColorBalanceRgbOp` mở rộng — 4-way (lift/gamma/gain + offset) + global chroma/contrast/brilliance,
      perceptual (nâng `ColorGradingOp`).
- [~] **D2.2** Working color space + input/output profile: `ColorSpaces` (ma trận RGB↔XYZ D65 cho
      sRGB/AdobeRGB/Rec2020/DisplayP3 + invert/mul 3x3) + `InputProfileOp` quy ảnh nguồn gamut rộng về
      working linear sRGB bằng ma trận 3x3, nối UI "Color Management" + test. **Đọc ICC nhúng ĐÃ XONG:**
      `IccProfileReader` (parse header + tag 'desc' v2/v4 mluc, đoán gamut) -> tự gợi ý Input Profile theo
      ICC ảnh (`DetectSpaceFromFile`), có test. Output ICC profile khi export + parse ICC -> ma trận đầy đủ CHƯA.
- [x] **D2.3** `VelviaOp` / saturation thông minh theo độ rực + luminance (giống module velvia).
- [x] **D2.4** `ColorContrastOp` — chỉnh tương phản trục a*/b* (green-magenta, blue-yellow) trong Lab.
- [x] **D2.5** `RgbLevelsOp` — levels (black/gray/white point per-channel + auto). Auto Levels:
      `AutoTone.AnalyzeLevels` (điểm đen/trắng theo phân vị 0.5%/99.5%) + nút "Auto Levels" trong UI.
      Per-channel R/G/B black/white/gamma (kế thừa master khi NaN, color grading kiểu film) + UI Expander. + test.
      **Auto Color** (`AutoTone.AnalyzeColorLevels`: căng dải động riêng từng kênh R/G/B -> khử ám màu, nút "Auto Color"). + test.

## D. PHASE D3 — Detail & correction, test được

- [x] **D3.1** `DiffuseOp` (diffuse or sharpen) — khuếch tán dẫn hướng Perona–Malik trên luminance:
      Amount dương = sharpen bám cạnh (không khuếch đại nhiễu), âm = denoise/làm mịn giữ cạnh; Iterations
      + EdgeSensitivity, vòng lặp theo scale. Nối UI Detail + DevelopModules + test.
- [x] **D3.2** `RawDenoiseOp` / chroma denoise nâng — `ChromaDenoiseOp` cross-bilateral (guide luminance,
      giữ cạnh, mượt chroma mạnh hơn ColorNR), bán kính theo scale, EdgeSensitivity. Nối UI Detail + test.
- [x] **D3.3** `HotPixelOp` — khử điểm chết/nóng (so 4 lân cận theo ngưỡng -> thay trung bình, có test).
- [x] **D3.4** `CaCorrectOp` — khử quang sai màu trục (lateral CA) theo co/giãn radial kênh R/B quanh tâm
      (bilinear, có test). Khác defringe (viền cục bộ).
- [x] **D3.5** `LiquifyOp` (diffuse/warp cơ bản) — biến dạng cục bộ bằng tập handle đẩy/kéo
      (tâm + vector dịch + bán kính, falloff (1-t²)², trường dịch cộng dồn). `IResizingOp` inverse-map
      lặp điểm-bất-động, toạ độ/bán kính chuẩn hoá (khớp proxy/full-res), kẹp mép không thủng. Đăng ký
      registry + module Geometry + test. UI kéo handle tương tác trên canvas (overlay mũi tên + vòng
      bán kính, nhóm "Liquify / Warp" trong Develop) ĐÃ XONG.

## E. PHASE D4 — Mask & local nâng cao, test được

- [x] **D4.1** Parametric mask đầy đủ — chọn vùng theo nhiều kênh (L, C, h, R, G, B) với upper/lower + feather,
      kiểu Darktable parametric masking (`ParametricMask`: 6 kênh band-pass giao nhau, hue wrap, invert; nối
      MaskedOp + LocalMask + DevelopPanel "+ Param"; có test).
- [x] **D4.2** Mask combine ops — union/intersect/difference giữa nhiều mask trên 1 instance: `MaskCombine`
      (intersect a*b / union a+b-ab / subtract a*(1-b)) + mask phụ luminance-range trong `MaskedOp` ("Refine"
      combo + Min/Max/Smooth trong Local Adjustments). Round-trip qua params. + test.
- [~] **D4.3** Path/Ellipse/Gradient mask có nhiều node + feather riêng — `PolygonMask` (đa giác nhiều đỉnh,
      ray-casting inside + feather theo khoảng cách tới biên, invert; UI "+ Polygon" click đặt đỉnh trên ảnh,
      có test). Ellipse nhiều node + per-node feather CHƯA (Radial hiện 1 tâm).
- [~] **D4.4** Multiple instances 1 module (vd 2 lần exposure khác mask) — pipeline đã hỗ trợ; Local
      Adjustments cho phép NHIỀU mask instance cùng/khác loại (theo maskId) + nút **Nhân bản mask** (⧉,
      `LocalMask.Clone` Id mới, round-trip qua maskId). Multiple instance cho op GLOBAL (vd 2 Curve) vẫn cần UI riêng.
- [x] **D4.5** Blend modes (normal/multiply/screen/overlay/...) + opacity cho mỗi op (Darktable "blending").

## F. PHASE D5 — RAW thật (cần native), tách riêng

> **BẬT LibRaw (không cần sửa code):** bỏ `libraw.dll` (x64, build C API) + DLL phụ thuộc vào thư mục
> `native/` cạnh solution. Build Host tự copy vào output (target `CopyNativeRaw`). Khi có DLL,
> `LibRawDecoder` tự đè đuôi RAW (demosaic thật 16-bit); không có -> dùng JPEG preview như cũ.
> Verify chữ ký P/Invoke + chất lượng demosaic trên file RAW thật trước khi phát hành.

- [~] **D5.1** Plugin LibRaw (P/Invoke) đăng ký đè `ImageDecoderRegistry` -> demosaic sensor 12-14 bit thật.
      **Scaffold ĐÃ XONG:** `LibRawNative` (P/Invoke gated bằng `NativeLibrary.TryLoad`, no-op khi thiếu DLL) +
      `LibRawImageConverter` (buffer→LinearImage, linear-gamma, có test) + `LibRawDecoder` (đăng ký SAU
      RawPreviewDecoder, tự fallback JPEG preview nếu native lỗi). Cần bundle `libraw.dll` + verify trên file RAW thật.
- [~] **D5.2** Demosaic chọn được (PPG/AMaZE/RCD) + WB as-shot từ metadata RAW.
      LibRaw mặc định dùng demosaic chất lượng cao + WB as-shot; chọn thuật toán cụ thể qua param CHƯA.
- [~] **D5.3** Highlight reconstruction — `HighlightReconstructionOp` khử ám màu vùng cháy (kéo kênh đã clip
      về trung tính theo độ sáng đỉnh, giữ brightness), chạy trên ảnh thường + RAW preview. UI Tone Mapping +
      test. Phục hồi từ kênh RAW chưa bão hoà thật (cần dữ liệu sensor) CHƯA.
- [ ] **D5.4** Input color profile (DCP/camera matrix) -> working space.
- [ ] **D5.5** Hỗ trợ đa định dạng RAW rộng (rawspeed-like) — phụ thuộc LibRaw.

## G. PHASE D6 — Lighttable/quản lý kiểu Darktable

- [x] **D6.1** History stack có thể copy/paste TỪNG module giữa ảnh (selective paste) — `DevelopModules`
      (gom OpType -> 15 module + thứ tự pipeline chuẩn) + `DevelopClipboard.PasteModulesTo` + context menu
      "Paste Settings (chọn module)" checkable. Merge giữ module không chọn, sắp xếp lại canonical. + test.
- [x] **D6.2** Styles có thể append (không thay thế) + chọn module khi áp — `DevelopModules.ApplyStyle`
      (append giữ edit hiện có, chỉ thay/thêm module style; replace thay toàn bộ) + `StyleService.ApplyToImageMerged`
      + checkbox "Append" trong StylePanel + StyleBatchAdapter mode append/replace. + test.
- [~] **D6.3** Duplicate / virtual copies (nhiều phiên bản edit của 1 ảnh) — cần model history per-version.
      **Named Snapshots ĐÃ XONG** (`IHistoryService.SaveSnapshot/ApplySnapshot/DeleteSnapshot/GetSnapshots`:
      lưu nhiều mốc edit có tên, bất biến, persist sidecar; UI Snapshots trong HistoryPanel + test). Virtual
      copy đầy đủ (nhiều bản song song trong grid) vẫn cần model per-version + selection-by-copy-id.
- [x] **D6.4** Tagging phân cấp + recently used + tag từ điển — `KeywordHelper.CountTags` (đếm tag/ảnh
      cho cây), `AppSettings.TagDictionary`/`RecentTags` + `ISettingsService.AddRecentTags` (chuẩn hoá +
      mở rộng tổ tiên + cap), trình sửa Keywords trong InfoPanel (chip thêm/gỡ, nhập phân cấp "/", gợi ý từ
      từ điển + recent), VisionTagger ghi recent khi lưu keyword. + test.
- [x] **D6.5** Culling nâng cao + đánh dấu reject hàng loạt — `IImageMetaService.SetRating/Label/PickMany`
      (gộp ghi sidecar 1 lần/folder), phím rating/flag/label áp cho TOÀN BỘ selection (không chỉ ảnh active),
      `WorkspaceFilter.HideRejected` + bộ lọc Pick/Reject/Hide-rejected trên top bar (luồng cull). + test.

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
12. D3.5 Liquify ✅ (engine warp + UI kéo handle + test), D4.4 instance UI.

## I. NGUYÊN TẮC THỰC HIỆN
- Mỗi op mới: `IEditOp` linear-light, thuần tham số, **có unit test**, đăng ký `EditOpRegistry`, nối DevelopPanel.
- Giữ build 0 warning; commit từng mục; smoke test sau mỗi nhóm UI.
- Việc cần model/native (D5, AI): code + pipeline + test logic ở đây, **inference/native verify trên máy thật**.
- Blend modes (D4.5) nên làm sớm vì nó là hạ tầng dùng lại cho mọi op sau.
