using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ImageTool.Core;
using ImageTool.Imaging;

namespace ImageTool.Host.Workspace;

// Liquify/Warp UI (D3.5). Handle lưu ở DevelopPanel, round-trip qua history như 1 LiquifyOp.
public partial class DevelopPanel
{
    private readonly List<LiquifyOp.Warp> _warps = new();
    private float _liquifyRadius = 0.15f;
    private CheckBox? _chkLiquifyActive;
    private TextBlock? _liquifyInfo;

    /// <summary>Bắn true khi bật Liquify (CenterPreview cho kéo handle), false khi tắt.</summary>
    public event EventHandler<bool>? LiquifyActivated;

    /// <summary>Bắn khi danh sách warp đổi (CenterPreview vẽ lại overlay).</summary>
    public event EventHandler? LiquifyChanged;

    /// <summary>CenterPreview đọc danh sách warp hiện tại để vẽ overlay.</summary>
    public IReadOnlyList<LiquifyOp.Warp> GetWarps() => _warps;

    private void BuildLiquifyUI(StackPanel host)
    {
        _chkLiquifyActive = new CheckBox
        {
            Content = "Bật Liquify (kéo trên ảnh để đẩy/kéo)", Foreground = Brushes.Gainsboro, FontSize = 11,
            Margin = new Thickness(0, 2, 0, 4)
        };
        _chkLiquifyActive.Checked += (_, _) => LiquifyActivated?.Invoke(this, true);
        _chkLiquifyActive.Unchecked += (_, _) => LiquifyActivated?.Invoke(this, false);
        host.Children.Add(_chkLiquifyActive);

        var row = MakeSliderRow("Brush Size", 0.03, 0.5, _liquifyRadius, "0.00", out var slider);
        slider.ValueChanged += (_, e) => { _liquifyRadius = (float)e.NewValue; };
        host.Children.Add(row);

        var btnUndo = new Button { Content = "↶ Xoá handle cuối", Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(0, 2, 0, 2) };
        btnUndo.Click += (_, _) =>
        {
            if (_warps.Count > 0)
            {
                _warps.RemoveAt(_warps.Count - 1);
                UpdateLiquifyInfo();
                LiquifyChanged?.Invoke(this, EventArgs.Empty);
                Commit();
            }
        };
        host.Children.Add(btnUndo);

        var btnClear = new Button { Content = "✕ Xoá tất cả handle", Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(0, 0, 0, 2) };
        btnClear.Click += (_, _) =>
        {
            if (_warps.Count > 0)
            {
                _warps.Clear();
                UpdateLiquifyInfo();
                LiquifyChanged?.Invoke(this, EventArgs.Empty);
                Commit();
            }
        };
        host.Children.Add(btnClear);

        _liquifyInfo = new TextBlock { Foreground = Brushes.Gray, FontSize = 10, Margin = new Thickness(0, 2, 0, 0) };
        host.Children.Add(_liquifyInfo);
        UpdateLiquifyInfo();
    }

    private void UpdateLiquifyInfo()
    {
        if (_liquifyInfo != null) _liquifyInfo.Text = $"{_warps.Count} handle";
    }

    /// <summary>CenterPreview gọi khi kéo xong 1 warp (tâm chuẩn hoá + vector dịch theo cạnh dài).</summary>
    public void AddWarp(float cx, float cy, float dx, float dy)
    {
        if (_currentPath == null || _history == null) return;
        _warps.Add(new LiquifyOp.Warp { Cx = cx, Cy = cy, Dx = dx, Dy = dy, Radius = _liquifyRadius });
        UpdateLiquifyInfo();
        LiquifyChanged?.Invoke(this, EventArgs.Empty);
        Commit();
    }

    /// <summary>Sinh LiquifyOp từ handle (gọi trong BuildOps). Rỗng nếu chưa có / không dịch.</summary>
    private void AppendLiquifyOp(List<EditOperation> ops)
    {
        if (_warps.Count == 0) return;
        var op = new LiquifyOp();
        op.Warps.AddRange(_warps);
        if (op.IsIdentity) return;
        ops.Add(Op(LiquifyOp.Type, "Liquify", op.ToParams()));
    }

    /// <summary>Nạp lại handle từ history (gọi trong LoadFor).</summary>
    private void LoadLiquify(string path)
    {
        _warps.Clear();
        var p = FindOp(path, LiquifyOp.Type);
        if (p != null)
            _warps.AddRange(LiquifyOp.FromParams(p).Warps);
        UpdateLiquifyInfo();
        LiquifyChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ClearLiquify()
    {
        _warps.Clear();
        if (_chkLiquifyActive != null) _chkLiquifyActive.IsChecked = false;
        UpdateLiquifyInfo();
        LiquifyChanged?.Invoke(this, EventArgs.Empty);
    }
}
