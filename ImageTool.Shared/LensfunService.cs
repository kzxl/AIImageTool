using System;
using System.IO;
using ImageTool.Imaging;

namespace ImageTool.Shared;

/// <summary>
/// Dịch vụ hiệu chỉnh ống kính TỰ ĐỘNG bằng lensfun (5.3): nạp database XML từ thư mục "lensfun/" cạnh
/// app (giống cách "native/" cho LibRaw), rồi dựng <see cref="LensProfileOp"/> cho 1 ảnh dựa trên tên
/// lens (EXIF LensModel) + tiêu cự. Nếu không có DB hoặc không khớp lens -> trả null (app dùng chỉnh tay).
///
/// DB nạp 1 lần (lazy) và cache. Cho phép truyền DB sẵn để unit test mà không cần file.
/// </summary>
public sealed class LensfunService
{
    private readonly object _lock = new();
    private LensfunDatabase? _db;
    private readonly string _dir;

    public LensfunService(string? lensfunDir = null)
    {
        _dir = lensfunDir ?? Path.Combine(AppContext.BaseDirectory, "lensfun");
    }

    /// <summary>Ctor cho test: bơm thẳng DB.</summary>
    public LensfunService(LensfunDatabase db)
    {
        _db = db;
        _dir = "";
    }

    /// <summary>True nếu có ít nhất 1 lens trong DB (đã nạp).</summary>
    public bool HasDatabase => GetDb().Lenses.Count > 0;

    private LensfunDatabase GetDb()
    {
        if (_db != null) return _db;
        lock (_lock)
        {
            _db ??= LensfunDatabase.LoadDirectory(_dir);
        }
        return _db;
    }

    /// <summary>
    /// Dựng op hiệu chỉnh cho 1 ảnh theo tên lens + tiêu cự (mm). Trả null nếu không khớp lens hoặc
    /// op rỗng (không có hệ số). Caller (DevelopPanel) chèn op vào history.
    /// </summary>
    public LensProfileOp? BuildOpFor(string? lensModel, float focalLengthMm,
        bool correctDistortion = true, bool correctVignetting = true)
    {
        var db = GetDb();
        var lens = db.FindLens(lensModel);
        if (lens == null) return null;

        var dist = correctDistortion ? LensfunDatabase.InterpolateDistortion(lens, focalLengthMm) : null;
        var vig = correctVignetting ? LensfunDatabase.InterpolateVignetting(lens, focalLengthMm) : null;
        if (dist == null && vig == null) return null;

        var op = LensProfileOp.FromCalib(dist, vig);
        op.CorrectDistortion = correctDistortion;
        op.CorrectVignetting = correctVignetting;
        return op.IsIdentity ? null : op;
    }

    /// <summary>Tên lens khớp được trong DB (để UI hiển thị "Đã nhận diện: ..."), null nếu không.</summary>
    public string? MatchLensName(string? lensModel) => GetDb().FindLens(lensModel)?.Model;
}
