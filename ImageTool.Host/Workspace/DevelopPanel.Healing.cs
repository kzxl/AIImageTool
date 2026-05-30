using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ImageTool.Core;
using ImageTool.Imaging;

namespace ImageTool.Host.Workspace;

// Healing/Clone brush UI (#6). Spots lưu ở DevelopPanel, round-trip qua history như 1 HealingOp.
public partial class DevelopPanel
{
    private readonly List<HealingOp.Spot> _healSpots = new();
    private HealingOp.HealMode _healMode = HealingOp.HealMode.Heal;
    private float _healRadius = 0.03f;
    private CheckBox? _chkHealActive;
    private TextBlock? _healInfo;

    /// <summary>Bắn true khi bật chế độ Heal (CenterPreview cho click chấm vết), false khi tắt.</summary>
    public event EventHandler<bool>? HealingModeChanged;

    /// <summary>Bán kính heal hiện tại (chuẩn hoá) — CenterPreview đọc để vẽ + auto-source.</summary>
    public float HealRadius => _healRadius;

    private void BuildHealingUI(StackPanel host)
    {
        _chkHealActive = new CheckBox
        {
            Content = "Bật Healing (click vào vết để xoá)", Foreground = Brushes.Gainsboro, FontSize = 11,
            Margin = new Thickness(0, 2, 0, 4)
        };
        _chkHealActive.Checked += (_, _) => HealingModeChanged?.Invoke(this, true);
        _chkHealActive.Unchecked += (_, _) => HealingModeChanged?.Invoke(this, false);
        host.Children.Add(_chkHealActive);

        var modeRow = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        modeRow.Children.Add(new TextBlock { Text = "Chế độ", Foreground = Brushes.Gray, FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
        var cmbMode = new ComboBox { Height = 22, Margin = new Thickness(6, 0, 0, 0) };
        cmbMode.Items.Add(new ComboBoxItem { Content = "Heal (vá liền)" });
        cmbMode.Items.Add(new ComboBoxItem { Content = "Clone (chép thẳng)" });
        cmbMode.SelectedIndex = 0;
        cmbMode.SelectionChanged += (_, _) =>
        {
            _healMode = cmbMode.SelectedIndex == 1 ? HealingOp.HealMode.Clone : HealingOp.HealMode.Heal;
            if (!_loading && _healSpots.Count > 0) Commit();
        };
        modeRow.Children.Add(cmbMode);
        host.Children.Add(modeRow);

        var row = MakeSliderRow("Brush Size", 0.005, 0.12, _healRadius, "0.000", out var slider);
        slider.ValueChanged += (_, e) => { _healRadius = (float)e.NewValue; };
        host.Children.Add(row);

        var btnUndo = new Button { Content = "↶ Xoá chấm cuối", Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(0, 2, 0, 2) };
        btnUndo.Click += (_, _) =>
        {
            if (_healSpots.Count > 0) { _healSpots.RemoveAt(_healSpots.Count - 1); UpdateHealInfo(); Commit(); }
        };
        host.Children.Add(btnUndo);

        _healInfo = new TextBlock { Foreground = Brushes.Gray, FontSize = 10, Margin = new Thickness(0, 2, 0, 0) };
        host.Children.Add(_healInfo);
        UpdateHealInfo();
    }

    private void UpdateHealInfo()
    {
        if (_healInfo != null) _healInfo.Text = $"{_healSpots.Count} chấm đã xoá";
    }

    /// <summary>CenterPreview gọi khi user click 1 điểm (toạ độ chuẩn hoá). Auto-pick nguồn lân cận sạch.</summary>
    public void AddHealSpot(float tx, float ty)
    {
        if (_currentPath == null || _history == null) return;
        // nguồn auto: dịch ngang 1 khoảng = 2.5×bán kính (về phía có chỗ trống trong khung).
        float dxn = _healRadius * 2.5f;
        float sx = tx - dxn >= _healRadius ? tx - dxn : tx + dxn;
        float sy = ty;
        sx = Math.Clamp(sx, 0f, 1f);
        _healSpots.Add(new HealingOp.Spot(tx, ty, sx, sy, _healRadius));
        UpdateHealInfo();
        Commit();
    }

    /// <summary>Sinh HealingOp từ spots (gọi trong BuildOps). Rỗng nếu chưa có chấm.</summary>
    private void AppendHealingOp(List<EditOperation> ops)
    {
        if (_healSpots.Count == 0) return;
        var op = new HealingOp { Mode = _healMode };
        op.Spots.AddRange(_healSpots);
        ops.Add(Op(HealingOp.Type, "Healing", op.ToParams()));
    }

    /// <summary>Nạp lại spots từ history (gọi trong LoadFor).</summary>
    private void LoadHealing(string path)
    {
        _healSpots.Clear();
        var p = FindOp(path, HealingOp.Type);
        if (p != null)
        {
            var op = HealingOp.FromParams(p);
            _healSpots.AddRange(op.Spots);
            _healMode = op.Mode;
        }
        UpdateHealInfo();
    }

    private void ClearHealing()
    {
        _healSpots.Clear();
        if (_chkHealActive != null) _chkHealActive.IsChecked = false;
        UpdateHealInfo();
    }
}
