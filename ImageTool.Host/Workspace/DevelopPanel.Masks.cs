using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ImageTool.Core;
using ImageTool.Imaging;

namespace ImageTool.Host.Workspace;

// Local Adjustments / Masking UI (6.4 brush + 6.7 full slider set per mask).
public partial class DevelopPanel
{
    private readonly List<LocalMask> _masks = new();
    private LocalMask? _activeMask;
    private StackPanel? _maskListPanel;
    private StackPanel? _maskEditPanel;
    private readonly Dictionary<string, Slider> _maskSliders = new(StringComparer.OrdinalIgnoreCase);
    private TextBlock? _maskHint;
    private Expander? _maskExpander;

    /// <summary>Bắn khi user chọn 1 brush mask để bắt đầu vẽ (CenterPreview lắng nghe). null = thôi vẽ.</summary>
    public event EventHandler<LocalMask?>? BrushMaskActivated;

    /// <summary>Mở rộng + cuộn tới nhóm Local Adjustments (phím M kiểu LR Masking module).</summary>
    public void FocusMasking()
    {
        if (_maskExpander == null) return;
        _maskExpander.IsExpanded = true;
        _maskExpander.BringIntoView();
    }

    /// <summary>Dựng nhóm "Local Adjustments" (gọi từ BuildUI).</summary>
    private void BuildMaskUI(StackPanel host)
    {
        var addRow = new WrapPanel { Margin = new Thickness(0, 2, 0, 4) };
        AddMaskButton(addRow, "+ Gradient", LinearGradientMask.Type);
        AddMaskButton(addRow, "+ Radial", RadialMask.Type);
        AddMaskButton(addRow, "+ Brush", BrushMask.Type);
        AddMaskButton(addRow, "+ Polygon", PolygonMask.Type);
        AddMaskButton(addRow, "+ Lum", LuminanceRangeMask.Type);
        AddMaskButton(addRow, "+ Color", ColorRangeMask.Type);
        AddMaskButton(addRow, "+ Param", ParametricMask.Type);
        var btnSubject = new Button { Content = "✦ AI Subject", Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(0, 0, 4, 4), FontSize = 11, ToolTip = "Tự chọn chủ thể bằng AI (tải model lần đầu)" };
        btnSubject.Click += (_, _) => RequestSubjectMask();
        addRow.Children.Add(btnSubject);
        AddMaskButton(addRow, "+ Sky", SkyMask.Type);
        host.Children.Add(addRow);

        _maskListPanel = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };
        host.Children.Add(_maskListPanel);

        _maskHint = new TextBlock
        {
            Text = "Thêm mask để chỉnh cục bộ. Brush: chọn mask rồi vẽ trên ảnh.",
            Foreground = Brushes.Gray, FontSize = 10, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 4)
        };
        host.Children.Add(_maskHint);

        _maskEditPanel = new StackPanel { Margin = new Thickness(0, 4, 0, 2) };
        host.Children.Add(_maskEditPanel);
    }

    private void AddMaskButton(Panel host, string label, string maskType)
    {
        var b = new Button { Content = label, Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(0, 0, 4, 4), FontSize = 11 };
        b.Click += (_, _) => AddMask(maskType);
        host.Children.Add(b);
    }

    private void AddMask(string maskType)
    {
        if (_currentPath == null || _history == null) return;
        var m = LocalMask.CreateDefault(maskType);
        _masks.Add(m);
        SelectMask(m);
        RefreshMaskList();
        Commit();
    }

    /// <summary>Bắn khi user bấm "AI Subject" — Host (có AiMaskService) lắng nghe, sinh mask rồi gọi AddRasterMask.</summary>
    public event EventHandler<string>? SubjectMaskRequested;

    private void RequestSubjectMask()
    {
        if (string.IsNullOrEmpty(_currentPath)) return;
        SubjectMaskRequested?.Invoke(this, _currentPath);
    }

    /// <summary>Host gọi lại sau khi AI sinh xong mask PNG: tạo 1 local mask kiểu Raster trỏ tới file đó.</summary>
    public void AddRasterMask(string maskFilePath, string name = "AI Subject")
    {
        if (_currentPath == null || _history == null) return;
        var m = new LocalMask
        {
            MaskType = RasterMask.Type,
            Name = name,
            MaskParams = new Dictionary<string, string> { ["maskFile"] = maskFilePath, ["invert"] = "false" },
        };
        _masks.Add(m);
        SelectMask(m);
        RefreshMaskList();
        Commit();
    }

    private void RemoveMask(LocalMask m)
    {
        _masks.Remove(m);
        if (_activeMask == m) SelectMask(null);
        RefreshMaskList();
        Commit();
    }

    private void SelectMask(LocalMask? m)
    {
        _activeMask = m;
        BuildMaskEditor();
        // Brush/Polygon mask đang chọn -> báo CenterPreview cho phép vẽ/đặt điểm.
        bool drawable = m != null && (m.MaskType == BrushMask.Type || m.MaskType == PolygonMask.Type);
        BrushMaskActivated?.Invoke(this, drawable ? m : null);
    }

    private void RefreshMaskList()
    {
        if (_maskListPanel == null) return;
        _maskListPanel.Children.Clear();
        foreach (var m in _masks)
        {
            var mm = m;
            var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
            var del = new Button { Content = "✕", Padding = new Thickness(5, 0, 5, 0), FontSize = 10, Margin = new Thickness(4, 0, 0, 0) };
            del.Click += (_, _) => RemoveMask(mm);
            DockPanel.SetDock(del, Dock.Right);
            var sel = new Button
            {
                Content = $"{m.Name}{(m.MaskType == BrushMask.Type ? "" : "")}",
                HorizontalContentAlignment = HorizontalAlignment.Left, FontSize = 11,
                Padding = new Thickness(6, 2, 6, 2),
                Background = m == _activeMask ? new SolidColorBrush(Color.FromRgb(0x3D, 0x7E, 0xFF)) : Brushes.Transparent,
                Foreground = Brushes.Gainsboro, BorderThickness = new Thickness(0)
            };
            sel.Click += (_, _) => { SelectMask(mm); RefreshMaskList(); };
            row.Children.Add(del);
            row.Children.Add(sel);
            _maskListPanel.Children.Add(row);
        }
        if (_maskHint != null)
            _maskHint.Visibility = _masks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BuildMaskEditor()
    {
        if (_maskEditPanel == null) return;
        _maskEditPanel.Children.Clear();
        _maskSliders.Clear();
        if (_activeMask == null) return;
        var m = _activeMask;

        _maskEditPanel.Children.Add(new TextBlock
        {
            Text = $"— {m.Name} —", Foreground = Brushes.Gray, FontSize = 10,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 2)
        });

        // Tham số riêng theo loại mask.
        switch (m.MaskType)
        {
            case BrushMask.Type:
                AddMaskGeomSlider(m, "radius", "Brush Size", 0.01, 0.3, 0.05);
                AddMaskGeomSlider(m, "hardness", "Hardness", 0, 0.99, 0.5);
                _maskEditPanel.Children.Add(new TextBlock
                {
                    Text = "Vẽ trực tiếp trên ảnh (giữ chuột kéo). Chuột phải = xoá nét.",
                    Foreground = Brushes.Gray, FontSize = 10, TextWrapping = TextWrapping.Wrap
                });
                break;
            case PolygonMask.Type:
                AddMaskGeomSlider(m, "feather", "Feather", 0, 0.5, 0.05);
                AddMaskInvertToggle(m);
                _maskEditPanel.Children.Add(new TextBlock
                {
                    Text = "Click trên ảnh để đặt các đỉnh đa giác (≥3 điểm). Vùng trong đa giác được chọn.",
                    Foreground = Brushes.Gray, FontSize = 10, TextWrapping = TextWrapping.Wrap
                });
                break;
            case RadialMask.Type:
                AddMaskGeomSlider(m, "feather", "Feather", 0.01, 1, 0.4);
                AddMaskInvertToggle(m);
                break;
            case LinearGradientMask.Type:
                AddMaskInvertToggle(m);
                break;
            case LuminanceRangeMask.Type:
                AddMaskGeomSlider(m, "min", "Range Min", 0, 1, 0);
                AddMaskGeomSlider(m, "max", "Range Max", 0, 1, 1);
                AddMaskGeomSlider(m, "smooth", "Smoothness", 0.001, 0.5, 0.1);
                break;
            case ColorRangeMask.Type:
                AddMaskGeomSlider(m, "hue", "Target Hue", 0, 360, 0, "0");
                AddMaskGeomSlider(m, "range", "Hue Range", 1, 90, 30, "0");
                AddMaskGeomSlider(m, "minSat", "Min Sat", 0, 1, 0.1);
                break;
            case RasterMask.Type:
                AddMaskInvertToggle(m); // AI mask: chỉ cho đảo vùng (chủ thể <-> nền)
                break;
            case SkyMask.Type:
                AddMaskGeomSlider(m, "strength", "Ưu tiên vị trí", 0, 1, 0.7);
                AddMaskGeomSlider(m, "smooth", "Smoothness", 0.001, 0.5, 0.15);
                break;
            case ParametricMask.Type:
                _maskEditPanel.Children.Add(new TextBlock
                {
                    Text = "Chọn vùng theo nhiều kênh (giao điều kiện). Để Min=0, Max=1 nếu không dùng kênh.",
                    Foreground = Brushes.Gray, FontSize = 10, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 2)
                });
                AddParamChannel(m, "l", "Lightness");
                AddParamChannel(m, "c", "Chroma");
                AddParamChannel(m, "h", "Hue");
                AddParamChannel(m, "r", "Red");
                AddParamChannel(m, "g", "Green");
                AddParamChannel(m, "b", "Blue");
                AddMaskInvertToggle(m);
                break;
        }

        // Bộ chỉnh đầy đủ Light/Color (6.7).
        AddMaskAdjSlider("Exposure", () => m.Exposure, v => m.Exposure = v, -5, 5, 0, "0.00");
        AddMaskAdjSlider("Contrast", () => m.Contrast, v => m.Contrast = v, -1, 1, 0);
        AddMaskAdjSlider("Highlights", () => m.Highlights, v => m.Highlights = v, -1, 1, 0);
        AddMaskAdjSlider("Shadows", () => m.Shadows, v => m.Shadows = v, -1, 1, 0);
        AddMaskAdjSlider("Whites", () => m.Whites, v => m.Whites = v, -1, 1, 0);
        AddMaskAdjSlider("Blacks", () => m.Blacks, v => m.Blacks = v, -1, 1, 0);
        AddMaskAdjSlider("Temp", () => m.Temp, v => m.Temp = v, -1, 1, 0);
        AddMaskAdjSlider("Tint", () => m.Tint, v => m.Tint = v, -1, 1, 0);
        AddMaskAdjSlider("Saturation", () => m.Saturation, v => m.Saturation = v, -1, 1, 0);
        AddMaskAdjSlider("Vibrance", () => m.Vibrance, v => m.Vibrance = v, -1, 1, 0);
        AddMaskAdjSlider("Clarity", () => m.Clarity, v => m.Clarity = v, -1, 1, 0);
        AddMaskAdjSlider("Sharpen", () => m.Sharpen, v => m.Sharpen = v, 0, 1, 0);

        // Blend mode + opacity (D4.5).
        var blendRow = new DockPanel { Margin = new Thickness(0, 6, 0, 2) };
        blendRow.Children.Add(new TextBlock { Text = "Blend", Foreground = Brushes.Gray, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Width = 80 });
        var cmbBlend = new ComboBox { Height = 22 };
        string[] modes = { "normal", "multiply", "screen", "overlay", "softlight", "hardlight", "lighten", "darken", "addition", "subtract", "difference", "linearlight" };
        foreach (var mode in modes) cmbBlend.Items.Add(new ComboBoxItem { Content = mode });
        cmbBlend.SelectedIndex = System.Math.Max(0, System.Array.IndexOf(modes, m.BlendMode));
        cmbBlend.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            m.BlendMode = (cmbBlend.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "normal";
            ScheduleCommit();
        };
        blendRow.Children.Add(cmbBlend);
        _maskEditPanel!.Children.Add(blendRow);

        AddMaskAdjSlider("Opacity", () => m.Opacity, v => m.Opacity = v, 0, 1, 1, "0.00");

        // Refine theo luminance range (D4.2): kết hợp mask phụ.
        var combineRow = new DockPanel { Margin = new Thickness(0, 6, 0, 2) };
        combineRow.Children.Add(new TextBlock { Text = "Refine", Foreground = Brushes.Gray, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Width = 80 });
        var cmbCombine = new ComboBox { Height = 22 };
        string[] combineModes = { "none", "intersect", "union", "subtract" };
        foreach (var cm in combineModes) cmbCombine.Items.Add(new ComboBoxItem { Content = cm });
        string curCombine = m.MaskParams.TryGetValue("combine", out var cc) ? cc : "none";
        cmbCombine.SelectedIndex = System.Math.Max(0, System.Array.IndexOf(combineModes, curCombine));
        cmbCombine.ToolTip = "Tinh chỉnh mask theo dải độ sáng (Darktable drawn+parametric): giao/hợp/trừ.";
        cmbCombine.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            m.MaskParams["combine"] = (cmbCombine.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "none";
            ScheduleCommit();
        };
        combineRow.Children.Add(cmbCombine);
        _maskEditPanel!.Children.Add(combineRow);

        AddMaskGeomSlider(m, "c_min", "  Refine Min", 0, 1, 0);
        AddMaskGeomSlider(m, "c_max", "  Refine Max", 0, 1, 1);
        AddMaskGeomSlider(m, "c_smooth", "  Refine Smooth", 0.001, 0.5, 0.1);
    }

    private void AddMaskGeomSlider(LocalMask m, string key, string label, double min, double max, double def, string fmt = "0.00")
    {
        double cur = m.MaskParams.TryGetValue(key, out var s) &&
                     double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : def;
        var row = MakeSliderRow(label, min, max, cur, fmt, out var slider);
        slider.ValueChanged += (_, e) =>
        {
            if (_loading) return;
            m.MaskParams[key] = e.NewValue.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            ScheduleCommit();
        };
        _maskEditPanel!.Children.Add(row);
    }

    /// <summary>1 hàng parametric: 3 slider gọn Min/Max/Feather cho 1 kênh (prefix l/c/h/r/g/b).</summary>
    private void AddParamChannel(LocalMask m, string prefix, string label)
    {
        _maskEditPanel!.Children.Add(new TextBlock
        {
            Text = label, Foreground = Brushes.Gainsboro, FontSize = 11, FontWeight = System.Windows.FontWeights.SemiBold,
            Margin = new Thickness(0, 4, 0, 0)
        });
        AddMaskGeomSlider(m, prefix + "Min", "  Min", 0, 1, 0);
        AddMaskGeomSlider(m, prefix + "Max", "  Max", 0, 1, 1);
        AddMaskGeomSlider(m, prefix + "Feather", "  Feather", 0.001, 0.5, 0.1);
    }

    private void AddMaskInvertToggle(LocalMask m)
    {
        var chk = new CheckBox
        {
            Content = "Invert mask", Foreground = Brushes.Gainsboro, FontSize = 11, Margin = new Thickness(0, 2, 0, 2),
            IsChecked = m.MaskParams.TryGetValue("invert", out var iv) && iv == "true"
        };
        chk.Checked += (_, _) => { m.MaskParams["invert"] = "true"; if (!_loading) ScheduleCommit(); };
        chk.Unchecked += (_, _) => { m.MaskParams["invert"] = "false"; if (!_loading) ScheduleCommit(); };
        _maskEditPanel!.Children.Add(chk);
    }

    private void AddMaskAdjSlider(string label, Func<float> get, Action<float> set, double min, double max, double def, string fmt = "0.00")
    {
        var row = MakeSliderRow(label, min, max, get(), fmt, out var slider);
        slider.ValueChanged += (_, e) =>
        {
            if (_loading) return;
            set((float)e.NewValue);
            ScheduleCommit();
        };
        _maskEditPanel!.Children.Add(row);
    }

    /// <summary>Tạo 1 hàng slider gọn (label + slider + value) dùng riêng cho mask editor.</summary>
    private Grid MakeSliderRow(string label, double min, double max, double cur, string fmt, out Slider slider)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });

        var lbl = new TextBlock { Text = label, Foreground = Brushes.Gainsboro, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(lbl, 0);
        slider = new Slider { Minimum = min, Maximum = max, Value = Math.Clamp(cur, min, max), VerticalAlignment = VerticalAlignment.Center, IsMoveToPointEnabled = true };
        Grid.SetColumn(slider, 1);
        var val = new TextBlock { Foreground = Brushes.Gray, FontSize = 10, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
        val.Text = cur.ToString(fmt, System.Globalization.CultureInfo.InvariantCulture);
        Grid.SetColumn(val, 2);
        var capturedSlider = slider;
        slider.ValueChanged += (_, e) => val.Text = e.NewValue.ToString(fmt, System.Globalization.CultureInfo.InvariantCulture);
        capturedSlider.MouseDoubleClick += (_, e) => { capturedSlider.Value = Math.Clamp(0, min, max); e.Handled = true; };

        grid.Children.Add(lbl);
        grid.Children.Add(slider);
        grid.Children.Add(val);
        return grid;
    }

    /// <summary>Sinh toàn bộ EditOperation MaskedOp từ danh sách mask (gọi ở cuối BuildOps).</summary>
    private void AppendMaskOps(List<EditOperation> ops)
    {
        foreach (var m in _masks)
            ops.AddRange(m.ToOperations());
    }

    /// <summary>Dựng lại danh sách mask từ chuỗi MaskedOp trong history (gọi trong LoadFor).</summary>
    private void LoadMasks(string path)
    {
        _masks.Clear();
        _activeMask = null;
        if (_history != null)
        {
            var stack = _history.GetStack(path);
            int pointer = _history.GetPointer(path);
            var byId = new Dictionary<string, LocalMask>(StringComparer.Ordinal);
            int order = 0;
            for (int i = 0; i < Math.Min(pointer, stack.Count); i++)
            {
                var op = stack[i];
                if (!string.Equals(op.OpType, MaskedOp.Type, StringComparison.OrdinalIgnoreCase)) continue;
                var p = op.Params;
                string maskType = p.TryGetValue("mask", out var mt) ? mt : "";
                if (string.IsNullOrEmpty(maskType)) continue;
                string innerType = p.TryGetValue("inner", out var it) ? it : "";
                string id = p.TryGetValue("maskId", out var mid) ? mid : ("auto" + order);

                if (!byId.TryGetValue(id, out var mask))
                {
                    mask = new LocalMask { Id = id, MaskType = maskType };
                    mask.Name = MaskDisplayName(maskType);
                    mask.MaskParams = ExtractMaskParams(maskType, p);
                    if (p.TryGetValue("blend", out var bl)) mask.BlendMode = bl;
                    if (p.TryGetValue("opacity", out var opStr) &&
                        float.TryParse(opStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var opv))
                        mask.Opacity = opv;
                    byId[id] = mask;
                    _masks.Add(mask);
                    order++;
                }
                mask.ApplyInner(innerType, p);
            }
        }
        RefreshMaskList();
        BuildMaskEditor();
        BrushMaskActivated?.Invoke(this, null);
    }

    private static string MaskDisplayName(string maskType) => maskType switch
    {
        LinearGradientMask.Type => "Gradient",
        RadialMask.Type => "Radial",
        BrushMask.Type => "Brush",
        PolygonMask.Type => "Polygon",
        LuminanceRangeMask.Type => "Luminance Range",
        ColorRangeMask.Type => "Color Range",
        RasterMask.Type => "AI Subject",
        SkyMask.Type => "Sky",
        ParametricMask.Type => "Parametric",
        _ => "Mask"
    };

    /// <summary>Lọc các key tham số hình học của mask (bỏ inner/mask/maskId + key của inner op).</summary>
    private static Dictionary<string, string> ExtractMaskParams(string maskType, IReadOnlyDictionary<string, string> p)
    {
        string[] keys = maskType switch
        {
            LinearGradientMask.Type => new[] { "x0", "y0", "x1", "y1", "invert" },
            RadialMask.Type => new[] { "cx", "cy", "rx", "ry", "feather", "invert" },
            BrushMask.Type => new[] { "radius", "hardness", "pts" },
            PolygonMask.Type => new[] { "pts", "feather", "invert" },
            LuminanceRangeMask.Type => new[] { "min", "max", "smooth" },
            ColorRangeMask.Type => new[] { "hue", "range", "minSat", "smooth" },
            RasterMask.Type => new[] { "maskFile", "invert" },
            SkyMask.Type => new[] { "strength", "smooth" },
            ParametricMask.Type => new[]
            {
                "lMin", "lMax", "lFeather", "cMin", "cMax", "cFeather", "hMin", "hMax", "hFeather",
                "rMin", "rMax", "rFeather", "gMin", "gMax", "gFeather", "bMin", "bMax", "bFeather", "invert"
            },
            _ => Array.Empty<string>()
        };
        var d = new Dictionary<string, string>();
        foreach (var k in keys) if (p.TryGetValue(k, out var v)) d[k] = v;
        // D4.2: tham số combine (mask phụ luminance-range) áp cho mọi loại mask.
        foreach (var k in new[] { "combine", "c_min", "c_max", "c_smooth" })
            if (p.TryGetValue(k, out var cv)) d[k] = cv;
        return d;
    }

    /// <summary>Thêm 1 điểm vào brush/polygon mask đang active (toạ độ chuẩn hoá) rồi commit (debounce).</summary>
    public void AppendBrushPoint(float nx, float ny)
    {
        if (_activeMask == null) return;
        if (_activeMask.MaskType != BrushMask.Type && _activeMask.MaskType != PolygonMask.Type) return;
        string cur = _activeMask.MaskParams.TryGetValue("pts", out var s) ? s : "";
        string pt = $"{nx.ToString("R", System.Globalization.CultureInfo.InvariantCulture)},{ny.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}";
        _activeMask.MaskParams["pts"] = string.IsNullOrEmpty(cur) ? pt : cur + ";" + pt;
        ScheduleCommit();
    }

    /// <summary>Reset toàn bộ mask (gọi trong BtnReset).</summary>
    private void ClearMasks()
    {
        _masks.Clear();
        _activeMask = null;
        RefreshMaskList();
        BuildMaskEditor();
        BrushMaskActivated?.Invoke(this, null);
    }
}
