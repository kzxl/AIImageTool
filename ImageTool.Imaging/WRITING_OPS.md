# Viết một Develop Op mới (IEditOp)

Tài liệu ngắn cho lập trình viên muốn thêm 1 hiệu ứng chỉnh sửa phi phá hủy vào pipeline.

## Khái niệm

Toàn bộ chỉnh sửa Develop là các `IEditOp` chạy trong **linear light** (float RGBA), nằm ở
project `ImageTool.Imaging`. Pipeline (`EditPipeline`) replay chuỗi op lên 1 bản clone của ảnh
gốc, nên op phải **thuần theo tham số**: cùng tham số + cùng input => cùng output.

```
file ảnh ──decode──> LinearImage gốc (bất biến)
                         │  clone
                         ▼
                   replay từng IEditOp  ──> ảnh kết quả ──> encode (preview/export)
```

## Bước 1 — Tạo class op

```csharp
public sealed class MyOp : IEditOp
{
    public const string Type = "MyOp";        // khớp EditOperation.OpType
    public string OpType => Type;

    public float Amount;                        // tham số (chuẩn hoá -1..1 hoặc 0..1)

    // Bỏ qua khi vô hại -> không phình history, không tốn CPU.
    public bool IsIdentity => MathF.Abs(Amount) < 1e-4f;

    public void Apply(LinearImage image, float scale)
    {
        if (IsIdentity) return;
        float amt = Math.Clamp(Amount, -1f, 1f);

        // Op pixel-wise: dùng ProcessPixels (tự song song theo hàng).
        image.ProcessPixels((ref float r, ref float g, ref float b, ref float a) =>
        {
            r += amt; g += amt; b += amt;
            if (r < 0f) r = 0f; if (g < 0f) g = 0f; if (b < 0f) b = 0f;
        });
    }

    // Serialize tham số ra dictionary string (để lưu history JSON).
    public Dictionary<string, string> ToParams() => new()
    {
        ["amount"] = Amount.ToString("R", CultureInfo.InvariantCulture),
    };

    // Dựng lại op từ params (khi replay/mở lại ảnh).
    public static MyOp FromParams(IReadOnlyDictionary<string, string> p)
        => new() { Amount = EditOpRegistry.F(p, "amount") };

    public static void Register(EditOpRegistry reg) => reg.Register(Type, FromParams);
}
```

## Bước 2 — Đăng ký vào registry

Thêm 1 dòng vào `EditOpRegistry.CreateDefault()` (file `EditOp.cs`):

```csharp
MyOp.Register(reg);
```

## Bước 3 — `scale` (chỉ op có bán kính)

- Op **pixel-wise** (exposure, curve, saturation): bỏ qua `scale`.
- Op có **bán kính theo pixel** (blur, sharpen, clarity, vignette-by-pixel): nhân bán kính theo
  `scale` để preview proxy khớp full-res. Ví dụ: `float radius = BaseRadius * scale;`

## Bước 4 — Op đổi kích thước ảnh (crop/rotate)

Cài `IResizingOp` thay vì sửa tại chỗ — trả `LinearImage` mới:

```csharp
public sealed class MyResizeOp : IResizingOp
{
    public void Apply(LinearImage image, float scale) { } // không dùng
    public LinearImage ApplyResize(LinearImage image, float scale) { /* trả ảnh mới */ }
}
```
`EditPipeline` tự phát hiện và thay ảnh working.

## Bước 5 — Local mask (tùy chọn)

Không cần sửa op. Bọc op bằng `MaskedOp` + 1 `IMaskGenerator` (LinearGradient/Radial/LumRange)
là op của bạn thành local adjustment. Xem `MaskedOp.cs`.

## Bước 6 — Nối UI

Trong `DevelopPanel.xaml.cs`:
- `BuildUI()`: thêm `AddSlider(group, "mykey", "My Op", min, max, def)`.
- `LoadFor()`: `SetVal("mykey", Param(path, MyOp.Type, "amount"));`
- `BuildOps()`: dựng `MyOp` từ slider, `if (!op.IsIdentity) ops.Add(Op(MyOp.Type, "My Op", op.ToParams()));`
  Đặt đúng vị trí trong **thứ tự canonical** (geometry → basic → tone → color → detail → effects).

## Bước 7 — Test

Thêm test vào `ImageTool.Tests` (xem `PipelineTests.cs`, `NewOpsTests.cs`):
- Identity không đổi pixel.
- Tham số round-trip (`ToParams` -> `FromParams`).
- Hành vi mong đợi (vd amount>0 làm sáng lên).
- Đăng ký trong registry (`reg.Has(MyOp.Type)`).

Chạy: `dotnet test ImageTool.Tests/ImageTool.Tests.csproj`

## Quy ước

- Mọi tính toán ở **linear**; chỉ dùng `ColorSpace.LinearToSrgb`/`SrgbToLinear` khi cần thao tác
  theo cảm nhận (curve, hue, tone-region).
- Cho phép giá trị > 1.0 (highlight headroom); chỉ clamp ở encode.
- `IsIdentity` đúng để pipeline bỏ qua op vô hại.
- Tham số chuẩn hoá `[-1..1]` hoặc `[0..1]` cho đồng nhất UI; ngoại lệ: Exposure (EV stops),
  Hue (độ 0..360).
