using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ImageTool.Core;
using ImageTool.Imaging;

namespace ImageTool.Host.Workspace;

/// <summary>
/// Panel Develop kiểu Lightroom. Các nhóm (Expander) thu gọn được: Basic/Tone/Presence/Color/
/// HSL/Detail/Effects/Geometry. Mỗi slider có ô nhập số trực tiếp + double-click reset.
/// Kéo slider -> debounce -> UpsertGroup -> HistoryChanged -> CenterPreview render lại.
/// </summary>
public partial class DevelopPanel : UserControl
{
    private IWorkspaceService? _workspace;
    private IHistoryService? _history;
    private DevelopRenderer? _renderer;
    private DevelopClipboard? _clipboard;
    private IStyleService? _styles;
    private string? _currentPath;
    private bool _loading;

    private readonly Dictionary<string, Slider> _sliders = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TextBox> _inputs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, double> _defaults = new(StringComparer.OrdinalIgnoreCase);

    // HSL state: 8 dải × (hue, sat, lum).
    private readonly float[] _hslHue = new float[HslMixerOp.Bands];
    private readonly float[] _hslSat = new float[HslMixerOp.Bands];
    private readonly float[] _hslLum = new float[HslMixerOp.Bands];
    private int _band;
    private ComboBox? _bandCombo;
    private string _lutPath = "";
    private TextBlock? _lutLabel;

    // Tone curve editor + channel selector.
    private CurveEditor? _curveEditor;
    private ComboBox? _curveChannel; // 0=RGB,1=R,2=G,3=B
    private CheckBox? _chkCurvePreserveHue; // D1.4
    private readonly string[] _curveData = { "0,0;1,1", "0,0;1,1", "0,0;1,1", "0,0;1,1" };

    // Color grading 3-way wheels + lum sliders. 0=Shadows,1=Midtones,2=Highlights,3=Global.
    private readonly ColorWheel[] _gradeWheels = new ColorWheel[4];
    private readonly float[] _gradeHue = new float[4];
    private readonly float[] _gradeSat = new float[4];

    // Crop rectangle (chuẩn hoá [0..1]). Mặc định full khung. Set bởi CenterPreview overlay.
    private float _cropX, _cropY, _cropW = 1f, _cropH = 1f;

    // B&W + Invert toggles.
    private CheckBox? _chkBw;
    private CheckBox? _chkInvert;
    private CheckBox? _chkFilmNeg;
    private CheckBox? _chkAiUpscale;
    private ComboBox? _cmbInputProfile; // D2.2 working/input color space

    // Auto WB gains (áp qua ChannelGainOp). 1,1,1 = không.
    private float _wbGainR = 1f, _wbGainG = 1f, _wbGainB = 1f;

    private readonly DispatcherTimer _debounce;
    private bool _pendingCommit;

    public DevelopPanel()
    {
        InitializeComponent();
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        _debounce.Tick += (s, e) => { _debounce.Stop(); if (_pendingCommit) { _pendingCommit = false; Commit(); } };

        BuildUI();
        SetEnabled(false);
    }

    public void Bind(IWorkspaceService workspace, IHistoryService history, DevelopRenderer? renderer = null,
                     DevelopClipboard? clipboard = null, IStyleService? styles = null)
    {
        _workspace = workspace;
        _history = history;
        _renderer = renderer;
        _clipboard = clipboard;
        _styles = styles;
        _workspace.ActiveImageChanged += (s, e) => Dispatcher.BeginInvoke(() => LoadFor(e.CurrentPath));
        RefreshPresetList();
        LoadFor(_workspace.ActiveImage);
    }

    /// <summary>Bắn khi crop rectangle thay đổi (load ảnh / reset). CenterPreview overlay lắng nghe để vẽ.</summary>
    public event EventHandler<(float X, float Y, float W, float H)>? CropChanged;

    /// <summary>Đặt crop rectangle (chuẩn hoá) từ overlay rồi commit. Clamp về [0..1] và kích thước tối thiểu.</summary>
    public void SetCropRect(float x, float y, float w, float h)
    {
        x = Math.Clamp(x, 0f, 1f);
        y = Math.Clamp(y, 0f, 1f);
        w = Math.Clamp(w, 0.02f, 1f - x);
        h = Math.Clamp(h, 0.02f, 1f - y);
        _cropX = x; _cropY = y; _cropW = w; _cropH = h;
        if (!_loading) Commit();
    }

    /// <summary>Trả crop rectangle hiện tại (chuẩn hoá).</summary>
    public (float X, float Y, float W, float H) GetCropRect() => (_cropX, _cropY, _cropW, _cropH);

    private void BuildUI()
    {
        // Histogram + cảnh báo clip (live) trên cùng.
        panelSliders.Children.Add(BuildHistogram());

        // Basic / White Balance
        var gWb = AddGroup("White Balance", true);
        AddSlider(gWb, "kelvin", "Temp (K)", 2000, 12000, 6500, "0");
        AddSlider(gWb, "temp", "Temp (fine)", -1, 1, 0);
        AddSlider(gWb, "tint", "Tint", -1, 1, 0);
        var wbBtnRow = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
        var btnAutoWb = new Button { Content = "Auto WB", Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 4, 0), ToolTip = "Tự cân bằng trắng (gray-world)" };
        btnAutoWb.Click += BtnAutoWb_Click;
        var btnPickWb = new Button { Content = "⊙ Pick", Padding = new Thickness(8, 3, 8, 3), ToolTip = "Eyedropper: bấm rồi click 1 điểm xám trung tính trên ảnh" };
        btnPickWb.Click += BtnPickWb_Click;
        wbBtnRow.Children.Add(btnAutoWb);
        wbBtnRow.Children.Add(btnPickWb);
        gWb.Children.Add(wbBtnRow);

        var gTone = AddGroup("Tone", true);
        AddSlider(gTone, "exposure", "Exposure", -5, 5, 0, "0.00");
        AddSlider(gTone, "contrast", "Contrast", -1, 1, 0);
        AddSlider(gTone, "highlights", "Highlights", -1, 1, 0);
        AddSlider(gTone, "shadows", "Shadows", -1, 1, 0);
        AddSlider(gTone, "whites", "Whites", -1, 1, 0);
        AddSlider(gTone, "blacks", "Blacks", -1, 1, 0);
        AddSlider(gTone, "filmic", "Filmic", 0, 1, 0);

        // Tone Mapping nâng cao (D1: Sigmoid + Filmic RGB)
        var gToneMap = AddGroup("Tone Mapping (scene-referred)", false);
        AddSlider(gToneMap, "sig_amt", "Sigmoid", 0, 1, 0);
        AddSlider(gToneMap, "sig_contrast", "Sigmoid Contrast", 0.5, 3, 1.5, "0.00");
        AddSlider(gToneMap, "filmrgb_amt", "Filmic RGB", 0, 1, 0);
        AddSlider(gToneMap, "filmrgb_white", "  White (EV)", 1, 8, 4, "0.0");
        AddSlider(gToneMap, "filmrgb_black", "  Black (EV)", -10, -1, -6, "0.0");
        AddSlider(gToneMap, "filmrgb_contrast", "  Contrast", 0.5, 2.5, 1.2, "0.00");
        AddSlider(gToneMap, "filmrgb_lat", "  Latitude", 0, 0.9, 0.2, "0.00");
        AddSlider(gToneMap, "filmrgb_sat", "  HL Saturation", -1, 1, 0);
        AddSlider(gToneMap, "hlrecon", "Highlight Recon", 0, 1, 0);

        // Tone Equalizer (D1.3) — chỉnh sáng theo 5 zone
        var gToneEq = AddGroup("Tone Equalizer", false);
        AddSlider(gToneEq, "teq_blacks", "Blacks", -1, 1, 0);
        AddSlider(gToneEq, "teq_shadows", "Shadows", -1, 1, 0);
        AddSlider(gToneEq, "teq_mid", "Midtones", -1, 1, 0);
        AddSlider(gToneEq, "teq_highlights", "Highlights", -1, 1, 0);
        AddSlider(gToneEq, "teq_whites", "Whites", -1, 1, 0);

        // Parametric curve
        var gParam = AddGroup("Parametric Curve", false);
        AddSlider(gParam, "pc_hi", "Highlights", -1, 1, 0);
        AddSlider(gParam, "pc_lt", "Lights", -1, 1, 0);
        AddSlider(gParam, "pc_dk", "Darks", -1, 1, 0);
        AddSlider(gParam, "pc_sh", "Shadows", -1, 1, 0);

        // Tone Curve (point editor) — 2.2
        var gCurve = AddGroup("Tone Curve", false);
        var chRow = new DockPanel { Margin = new Thickness(0, 2, 0, 4) };
        chRow.Children.Add(new TextBlock { Text = "Kênh", Foreground = Brushes.Gray, FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
        _curveChannel = new ComboBox { Height = 22, Margin = new Thickness(6, 0, 0, 0) };
        foreach (var n in new[] { "RGB", "Red", "Green", "Blue" })
            _curveChannel.Items.Add(new ComboBoxItem { Content = n });
        _curveChannel.SelectedIndex = 0;
        _curveChannel.SelectionChanged += CurveChannel_SelectionChanged;
        chRow.Children.Add(_curveChannel);
        gCurve.Children.Add(chRow);
        _curveEditor = new CurveEditor { Margin = new Thickness(0, 2, 0, 4) };
        _curveEditor.CurveChanged += CurveEditor_Changed;
        gCurve.Children.Add(_curveEditor);
        var curveHint = new TextBlock
        {
            Text = "Kéo điểm • double-click thêm/xoá • phải-chuột xoá",
            Foreground = Brushes.Gray, FontSize = 10, TextWrapping = TextWrapping.Wrap
        };
        gCurve.Children.Add(curveHint);

        _chkCurvePreserveHue = new CheckBox
        {
            Content = "Preserve hue (master theo luminance)", Foreground = Brushes.Gainsboro, FontSize = 11,
            Margin = new Thickness(0, 4, 0, 2),
            ToolTip = "Đường master áp lên độ sáng và scale RGB giữ hue — tránh dịch màu ở vùng rực."
        };
        _chkCurvePreserveHue.Checked += (_, _) => { if (!_loading) ScheduleCommit(); };
        _chkCurvePreserveHue.Unchecked += (_, _) => { if (!_loading) ScheduleCommit(); };
        gCurve.Children.Add(_chkCurvePreserveHue);

        var gPres = AddGroup("Presence", true);
        AddSlider(gPres, "vibrance", "Vibrance", -1, 1, 0);
        AddSlider(gPres, "saturation", "Saturation", -1, 1, 0);
        AddSlider(gPres, "clarity", "Clarity", -1, 1, 0);
        AddSlider(gPres, "texture", "Texture", -1, 1, 0);
        AddSlider(gPres, "dehaze", "Dehaze", -1, 1, 0);
        AddSlider(gPres, "velvia", "Velvia", 0, 1, 0);

        // Levels (D2.5)
        var gLevels = AddGroup("Levels", false);
        AddSlider(gLevels, "lvl_black", "Black point", 0, 0.5, 0, "0.00");
        AddSlider(gLevels, "lvl_gamma", "Gamma", 0.2, 3, 1, "0.00");
        AddSlider(gLevels, "lvl_white", "White point", 0.5, 1, 1, "0.00");
        var btnAutoLevels = new Button { Content = "Auto Levels", Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 2, 0, 2), HorizontalAlignment = HorizontalAlignment.Left };
        btnAutoLevels.Click += BtnAutoLevels_Click;
        gLevels.Children.Add(btnAutoLevels);
        // Per-channel (D2.5): black/white/gamma riêng cho R/G/B (color grading kiểu film).
        var expLvlCh = new Expander { Header = "Per-channel R/G/B", Foreground = Brushes.Gainsboro, FontSize = 11, Margin = new Thickness(0, 2, 0, 2) };
        var gLvlCh = new StackPanel();
        AddSlider(gLvlCh, "lvl_blackR", "R Black", 0, 0.5, 0, "0.00");
        AddSlider(gLvlCh, "lvl_whiteR", "R White", 0.5, 1, 1, "0.00");
        AddSlider(gLvlCh, "lvl_gammaR", "R Gamma", 0.2, 3, 1, "0.00");
        AddSlider(gLvlCh, "lvl_blackG", "G Black", 0, 0.5, 0, "0.00");
        AddSlider(gLvlCh, "lvl_whiteG", "G White", 0.5, 1, 1, "0.00");
        AddSlider(gLvlCh, "lvl_gammaG", "G Gamma", 0.2, 3, 1, "0.00");
        AddSlider(gLvlCh, "lvl_blackB", "B Black", 0, 0.5, 0, "0.00");
        AddSlider(gLvlCh, "lvl_whiteB", "B White", 0.5, 1, 1, "0.00");
        AddSlider(gLvlCh, "lvl_gammaB", "B Gamma", 0.2, 3, 1, "0.00");
        expLvlCh.Content = gLvlCh;
        gLevels.Children.Add(expLvlCh);

        // Color Balance RGB 4-way (D2.1)
        var gCbr = AddGroup("Color Balance RGB", false);
        AddSlider(gCbr, "cbr_liftHue", "Shadow Hue", 0, 360, 0, "0");
        AddSlider(gCbr, "cbr_liftSat", "Shadow Sat", 0, 1, 0);
        AddSlider(gCbr, "cbr_gammaHue", "Mid Hue", 0, 360, 0, "0");
        AddSlider(gCbr, "cbr_gammaSat", "Mid Sat", 0, 1, 0);
        AddSlider(gCbr, "cbr_gainHue", "Highlight Hue", 0, 360, 0, "0");
        AddSlider(gCbr, "cbr_gainSat", "Highlight Sat", 0, 1, 0);
        AddSlider(gCbr, "cbr_chroma", "Global Chroma", -1, 1, 0);
        AddSlider(gCbr, "cbr_contrast", "Global Contrast", -1, 1, 0);

        // Color Contrast (D2.4)
        var gCc = AddGroup("Color Contrast (Lab)", false);
        AddSlider(gCc, "cc_ga", "Green ↔ Magenta", -1, 1, 0);
        AddSlider(gCc, "cc_by", "Blue ↔ Yellow", -1, 1, 0);

        // HSL
        var gHsl = AddGroup("HSL / Color Mixer", false);
        var bandRow = new DockPanel { Margin = new Thickness(0, 2, 0, 4) };
        bandRow.Children.Add(new TextBlock { Text = "Dải màu", Foreground = Brushes.Gray, FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
        _bandCombo = new ComboBox { Height = 22, Margin = new Thickness(6, 0, 0, 0) };
        foreach (var n in new[] { "Red", "Orange", "Yellow", "Green", "Aqua", "Blue", "Purple", "Magenta" })
            _bandCombo.Items.Add(new ComboBoxItem { Content = n });
        _bandCombo.SelectedIndex = 0;
        _bandCombo.SelectionChanged += BandCombo_SelectionChanged;
        gHsl.Children.Add(bandRow);
        bandRow.Children.Add(_bandCombo);
        AddSlider(gHsl, "hsl_hue", "Hue", -1, 1, 0);
        AddSlider(gHsl, "hsl_sat", "Saturation", -1, 1, 0);
        AddSlider(gHsl, "hsl_lum", "Luminance", -1, 1, 0);

        // Color grading (split toning đơn giản)
        var gSplit = AddGroup("Split Toning", false);
        AddSlider(gSplit, "st_hiHue", "HL Hue", 0, 360, 0, "0");
        AddSlider(gSplit, "st_hiSat", "HL Sat", 0, 1, 0);
        AddSlider(gSplit, "st_shHue", "SH Hue", 0, 360, 0, "0");
        AddSlider(gSplit, "st_shSat", "SH Sat", 0, 1, 0);
        AddSlider(gSplit, "st_bal", "Balance", -1, 1, 0);

        // Color Grading 3-way wheels — 3.3
        var gGrade = AddGroup("Color Grading", false);
        string[] zoneNames = { "Shadows", "Midtones", "Highlights", "Global" };
        var wheelRow = new UniformGrid { Columns = 2, Margin = new Thickness(0, 2, 0, 4) };
        for (int z = 0; z < 4; z++)
        {
            int zi = z;
            var cell = new StackPanel { Margin = new Thickness(2) };
            cell.Children.Add(new TextBlock { Text = zoneNames[z], Foreground = Brushes.Gainsboro, FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center });
            var wheel = new ColorWheel { HorizontalAlignment = HorizontalAlignment.Center };
            wheel.ColorChanged += (_, hs) => { _gradeHue[zi] = hs.hue; _gradeSat[zi] = hs.sat; if (!_loading) ScheduleCommit(); };
            _gradeWheels[z] = wheel;
            cell.Children.Add(wheel);
            wheelRow.Children.Add(cell);
        }
        gGrade.Children.Add(wheelRow);
        AddSlider(gGrade, "cg_sh_lum", "Shadow Lum", -1, 1, 0);
        AddSlider(gGrade, "cg_mid_lum", "Midtone Lum", -1, 1, 0);
        AddSlider(gGrade, "cg_hi_lum", "Highlight Lum", -1, 1, 0);
        AddSlider(gGrade, "cg_blend", "Blending", 0, 1, 0.5, "0.00");

        // Selective color (dịch 1 dải hue sang hue khác)
        var gSel = AddGroup("Selective Color", false);
        AddSlider(gSel, "sel_src", "Source Hue", 0, 360, 0, "0");
        AddSlider(gSel, "sel_tgt", "Target Hue", 0, 360, 0, "0");
        AddSlider(gSel, "sel_tol", "Tolerance", 1, 90, 30, "0");
        AddSlider(gSel, "sel_str", "Strength", 0, 1, 0);

        // Color unify (kéo toàn ảnh về 1 tông màu — port ColorLab non-destructive)
        var gUnify = AddGroup("Color Unify", false);
        AddSlider(gUnify, "uni_hue", "Target Hue", 0, 360, 0, "0");
        AddSlider(gUnify, "uni_sat", "Target Sat", 0, 1, 0.5, "0.00");
        AddSlider(gUnify, "uni_int", "Intensity", 0, 1, 0);

        // 3D LUT (.cube)
        var gLut = AddGroup("3D LUT (.cube)", false);
        var lutRow = new DockPanel { Margin = new Thickness(0, 2, 0, 4) };
        _lutLabel = new TextBlock { Text = "(chưa chọn LUT)", Foreground = Brushes.Gray, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var btnLut = new Button { Content = "Chọn...", Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(6, 0, 0, 0) };
        var btnLutClear = new Button { Content = "✕", Padding = new Thickness(6, 3, 6, 3), Margin = new Thickness(4, 0, 0, 0) };
        DockPanel.SetDock(btnLut, Dock.Right);
        DockPanel.SetDock(btnLutClear, Dock.Right);
        btnLut.Click += BtnLutPick_Click;
        btnLutClear.Click += (_, _) => { _lutPath = ""; if (_lutLabel != null) _lutLabel.Text = "(chưa chọn LUT)"; Commit(); };
        lutRow.Children.Add(btnLutClear);
        lutRow.Children.Add(btnLut);
        lutRow.Children.Add(_lutLabel);
        gLut.Children.Add(lutRow);
        AddSlider(gLut, "lut_intensity", "Intensity", 0, 1, 1);

        // Detail
        var gDetail = AddGroup("Detail", false);
        AddSlider(gDetail, "sharpen", "Sharpen", 0, 1, 0);
        AddSlider(gDetail, "sharpenRadius", "Sharpen Radius", 0.5, 3, 1, "0.0");
        AddSlider(gDetail, "sharpenMask", "Sharpen Masking", 0, 1, 0);
        AddSlider(gDetail, "lumaNR", "Luminance NR", 0, 1, 0);
        AddSlider(gDetail, "colorNR", "Color NR", 0, 1, 0);
        AddSlider(gDetail, "chromaNR", "Chroma NR (edge)", 0, 1, 0);
        AddSlider(gDetail, "diffuse", "Diffuse/Sharpen", -1, 1, 0);
        AddSlider(gDetail, "defrPurple", "Defringe Purple", 0, 1, 0);
        AddSlider(gDetail, "defrGreen", "Defringe Green", 0, 1, 0);
        AddSlider(gDetail, "hotpix", "Hot Pixel", 0, 1, 0);
        AddSlider(gDetail, "hotpixThr", "Hot Pixel Thr", 0, 1, 0.5);
        AddSlider(gDetail, "caRed", "CA Red/Cyan", -1, 1, 0);
        AddSlider(gDetail, "caBlue", "CA Blue/Yellow", -1, 1, 0);
        AddSlider(gDetail, "aiDenoise", "AI Denoise", 0, 1, 0);
        _chkAiUpscale = new CheckBox { Content = "AI Upscale 4x (khi export)", Foreground = Brushes.Gainsboro, FontSize = 12, Margin = new Thickness(0, 4, 0, 2), ToolTip = "Phóng to 4x bằng AI lúc export (cần model Upscaler)" };
        _chkAiUpscale.Checked += (_, _) => { if (!_loading) ScheduleCommit(); };
        _chkAiUpscale.Unchecked += (_, _) => { if (!_loading) ScheduleCommit(); };
        gDetail.Children.Add(_chkAiUpscale);

        // Effects
        var gFx = AddGroup("Effects", false);
        AddSlider(gFx, "vignette", "Vignette", -1, 1, 0);
        AddSlider(gFx, "grain", "Grain", 0, 1, 0);
        AddSlider(gFx, "glow", "Glow / Soften", 0, 1, 0);
        _chkInvert = new CheckBox { Content = "Negative / Invert", Foreground = Brushes.Gainsboro, FontSize = 12, Margin = new Thickness(0, 4, 0, 2) };
        _chkInvert.Checked += (_, _) => { if (!_loading) ScheduleCommit(); };
        _chkInvert.Unchecked += (_, _) => { if (!_loading) ScheduleCommit(); };
        gFx.Children.Add(_chkInvert);

        // Film Negative (negadoctor) — chuyển scan phim âm bản thành dương bản.
        var gFilm = AddGroup("Film Negative", false);
        _chkFilmNeg = new CheckBox { Content = "Bật Film Negative (scan phim âm bản)", Foreground = Brushes.Gainsboro, FontSize = 12, Margin = new Thickness(0, 2, 0, 4) };
        _chkFilmNeg.Checked += (_, _) => { if (!_loading) ScheduleCommit(); };
        _chkFilmNeg.Unchecked += (_, _) => { if (!_loading) ScheduleCommit(); };
        gFilm.Children.Add(_chkFilmNeg);
        AddSlider(gFilm, "film_rbase", "Base R", 0.02, 1, 0.50, "0.00");
        AddSlider(gFilm, "film_gbase", "Base G", 0.02, 1, 0.30, "0.00");
        AddSlider(gFilm, "film_bbase", "Base B", 0.02, 1, 0.18, "0.00");
        AddSlider(gFilm, "film_gamma", "Contrast (gamma)", 0.3, 3, 1, "0.00");
        AddSlider(gFilm, "film_exposure", "Exposure", 0.1, 4, 1, "0.00");
        var btnPickBase = new Button { Content = "Pick film base (click mép phim)", Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 2, 0, 2), HorizontalAlignment = HorizontalAlignment.Left };
        btnPickBase.Click += BtnPickFilmBase_Click;
        gFilm.Children.Add(btnPickBase);
        gFilm.Children.Add(new TextBlock { Text = "Mẹo: chọn Base bằng vùng mép phim trống (sáng nhất).", FontSize = 10, Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });

        // Black & White
        var gBw = AddGroup("Black & White", false);
        _chkBw = new CheckBox { Content = "Chuyển đen trắng", Foreground = Brushes.Gainsboro, FontSize = 12, Margin = new Thickness(0, 2, 0, 4) };
        _chkBw.Checked += (_, _) => { if (!_loading) ScheduleCommit(); };
        _chkBw.Unchecked += (_, _) => { if (!_loading) ScheduleCommit(); };
        gBw.Children.Add(_chkBw);
        AddSlider(gBw, "bw_r", "Red mix", 0, 1, 0.299, "0.00");
        AddSlider(gBw, "bw_g", "Green mix", 0, 1, 0.587, "0.00");
        AddSlider(gBw, "bw_b", "Blue mix", 0, 1, 0.114, "0.00");
        AddSlider(gBw, "bw_toneHue", "Tone Hue", 0, 360, 0, "0");
        AddSlider(gBw, "bw_toneStr", "Tone Strength", 0, 1, 0);

        // Geometry
        var gGeo = AddGroup("Geometry", false);
        var rotRow = new DockPanel { Margin = new Thickness(0, 2, 0, 4) };
        var btnRotL = new Button { Content = "⟲ 90°", Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 4, 0) };
        var btnRotR = new Button { Content = "⟳ 90°", Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 4, 0) };
        var btnFlipH = new Button { Content = "⇋ FlipH", Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 4, 0) };
        var btnFlipV = new Button { Content = "⇅ FlipV", Padding = new Thickness(8, 3, 8, 3) };
        btnRotL.Click += (_, _) => RotateBy(-1);
        btnRotR.Click += (_, _) => RotateBy(1);
        btnFlipH.Click += (_, _) => ToggleFlip(true);
        btnFlipV.Click += (_, _) => ToggleFlip(false);
        rotRow.Children.Add(btnRotL);
        rotRow.Children.Add(btnRotR);
        rotRow.Children.Add(btnFlipH);
        rotRow.Children.Add(btnFlipV);
        gGeo.Children.Add(rotRow);
        AddSlider(gGeo, "straighten", "Straighten", -45, 45, 0, "0.0");
        AddSlider(gGeo, "persp_v", "Perspective V", -1, 1, 0);
        AddSlider(gGeo, "persp_h", "Perspective H", -1, 1, 0);
        AddSlider(gGeo, "persp_scale", "Persp Scale", 0.5, 2, 1, "0.00");
        AddSlider(gGeo, "lens_k1", "Lens Distortion", -0.5, 0.5, 0, "0.00");
        AddSlider(gGeo, "lens_k2", "Lens Distortion 2", -0.5, 0.5, 0, "0.00");
        AddSlider(gGeo, "lens_vig", "Lens Vignette Fix", 0, 1, 0);

        // Color Management (D2.2): input/working color space.
        var gCm = AddGroup("Color Management", false);
        var cmRow = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
        cmRow.Children.Add(new TextBlock { Text = "Input Profile", Foreground = Brushes.Gainsboro, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Width = 90 });
        _cmbInputProfile = new ComboBox { Height = 22 };
        foreach (var n in new[] { "sRGB", "AdobeRGB", "Rec2020", "DisplayP3" })
            _cmbInputProfile.Items.Add(new ComboBoxItem { Content = n });
        _cmbInputProfile.SelectedIndex = 0;
        _cmbInputProfile.SelectionChanged += (_, _) => { if (!_loading) ScheduleCommit(); };
        _cmbInputProfile.ToolTip = "Diễn giải ảnh theo gamut này rồi quy về working sRGB (D65). sRGB = không đổi.";
        cmRow.Children.Add(_cmbInputProfile);
        gCm.Children.Add(cmRow);

        // Local Adjustments / Masking (6.4 brush + 6.7 full slider set)
        var gMask = AddGroup("Local Adjustments", false);
        _maskExpander = gMask.Parent as Expander;
        BuildMaskUI(gMask);

        // Healing / Clone brush (#6)
        var gHeal = AddGroup("Healing / Clone", false);
        BuildHealingUI(gHeal);

        // Liquify / Warp (D3.5)
        var gLiquify = AddGroup("Liquify / Warp", false);
        BuildLiquifyUI(gLiquify);
    }

    /// <summary>Tạo 1 nhóm thu gọn được (Expander) và trả về panel con để thêm slider.</summary>
    private StackPanel AddGroup(string header, bool expanded)
    {
        var content = new StackPanel { Margin = new Thickness(2, 4, 2, 6) };
        var exp = new Expander
        {
            Header = header,
            IsExpanded = expanded,
            Foreground = Brushes.Gainsboro,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 2, 0, 2),
            Content = content
        };
        panelSliders.Children.Add(exp);
        return content;
    }

    private void AddSlider(Panel host, string key, string label, double min, double max, double def, string fmt = "0.00")
    {
        _defaults[key] = def;
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });

        var lbl = new TextBlock { Text = label, Foreground = Brushes.Gainsboro, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(lbl, 0);

        var slider = new Slider
        {
            Minimum = min, Maximum = max, Value = def,
            SmallChange = (max - min) / 100.0, LargeChange = (max - min) / 10.0,
            VerticalAlignment = VerticalAlignment.Center, IsMoveToPointEnabled = true, Tag = key
        };
        Grid.SetColumn(slider, 1);

        var input = new TextBox
        {
            Text = def.ToString(fmt, CultureInfo.InvariantCulture),
            FontSize = 11, TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0),
            Tag = fmt, BorderThickness = new Thickness(0)
        };
        Grid.SetColumn(input, 2);

        slider.ValueChanged += (s, e) =>
        {
            if (!input.IsKeyboardFocused) input.Text = e.NewValue.ToString(fmt, CultureInfo.InvariantCulture);
            if (_loading) return;
            ScheduleCommit();
        };
        // Nhập số trực tiếp -> cập nhật slider khi Enter hoặc rời focus.
        input.KeyDown += (s, e) => { if (e.Key == Key.Enter) CommitInput(slider, input, min, max); };
        input.LostFocus += (s, e) => CommitInput(slider, input, min, max);
        // Double-click reset.
        lbl.MouseLeftButtonDown += (s, e) => { if (e.ClickCount == 2) slider.Value = def; };
        slider.MouseDoubleClick += (s, e) => { slider.Value = def; e.Handled = true; };

        grid.Children.Add(lbl);
        grid.Children.Add(slider);
        grid.Children.Add(input);
        host.Children.Add(grid);

        _sliders[key] = slider;
        _inputs[key] = input;
    }

    private static void CommitInput(Slider slider, TextBox input, double min, double max)
    {
        if (double.TryParse(input.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            slider.Value = Math.Clamp(v, min, max);
        else
            input.Text = slider.Value.ToString((string)input.Tag, CultureInfo.InvariantCulture);
    }

    private void ScheduleCommit()
    {
        _pendingCommit = true;
        _debounce.Stop();
        _debounce.Start();
    }

    private void SetVal(string key, double v) { if (_sliders.TryGetValue(key, out var s) ) s.Value = v; }
    private double GetVal(string key) => _sliders.TryGetValue(key, out var s) ? s.Value : 0;

    /// <summary>Giá trị slider per-channel; trả NaN nếu đang ở mặc định (kênh "kế thừa" master).</summary>
    private float ChVal(string key, double identity)
    {
        double v = GetVal(key);
        return Math.Abs(v - identity) < 1e-4 ? float.NaN : (float)v;
    }

    /// <summary>Đọc param per-channel của Levels từ history; trả identity nếu key không có.</summary>
    private static double LvlCh(IReadOnlyDictionary<string, string>? p, string key, double identity)
        => p != null && p.TryGetValue(key, out var s) &&
           double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : identity;

    private void LoadFor(string? path)
    {
        _currentPath = path;
        bool ok = !string.IsNullOrEmpty(path) && _history != null;
        SetEnabled(ok);
        if (!ok) return;

        _loading = true;
        var b = FindOp(path!, DevelopBasicOp.Type) is { } bp ? DevelopBasicOp.FromParams(bp) : null;
        SetVal("temp", b?.Temp ?? 0);
        SetVal("tint", b?.Tint ?? 0);
        SetVal("exposure", b?.Exposure ?? 0);
        SetVal("contrast", b?.Contrast ?? 0);
        SetVal("highlights", b?.Highlights ?? 0);
        SetVal("shadows", b?.Shadows ?? 0);
        SetVal("whites", b?.Whites ?? 0);
        SetVal("blacks", b?.Blacks ?? 0);
        SetVal("vibrance", b?.Vibrance ?? 0);
        SetVal("saturation", b?.Saturation ?? 0);

        SetVal("filmic", Param(path!, FilmicOp.Type, "amount"));

        // Tone Mapping nâng cao (D1)
        SetVal("sig_amt", Param(path!, SigmoidOp.Type, "amount"));
        var sigP = FindOp(path!, SigmoidOp.Type);
        SetVal("sig_contrast", sigP != null ? Param(path!, SigmoidOp.Type, "contrast") : 1.5);
        SetVal("filmrgb_amt", Param(path!, FilmicRgbOp.Type, "amount"));
        var frgbP = FindOp(path!, FilmicRgbOp.Type);
        SetVal("filmrgb_white", frgbP != null ? Param(path!, FilmicRgbOp.Type, "white") : 4);
        SetVal("filmrgb_black", frgbP != null ? Param(path!, FilmicRgbOp.Type, "black") : -6);
        SetVal("filmrgb_contrast", frgbP != null ? Param(path!, FilmicRgbOp.Type, "contrast") : 1.2);
        SetVal("filmrgb_lat", frgbP != null ? Param(path!, FilmicRgbOp.Type, "latitude") : 0.2);
        SetVal("filmrgb_sat", Param(path!, FilmicRgbOp.Type, "sat"));
        SetVal("teq_blacks", Param(path!, ToneEqualizerOp.Type, "blacks"));
        SetVal("teq_shadows", Param(path!, ToneEqualizerOp.Type, "shadows"));
        SetVal("teq_mid", Param(path!, ToneEqualizerOp.Type, "mid"));
        SetVal("teq_highlights", Param(path!, ToneEqualizerOp.Type, "highlights"));
        SetVal("teq_whites", Param(path!, ToneEqualizerOp.Type, "whites"));
        SetVal("dehaze", Param(path!, DehazeOp.Type, "amount"));

        // D2 color science
        SetVal("velvia", Param(path!, VelviaOp.Type, "amount"));
        var lvlP = FindOp(path!, RgbLevelsOp.Type);
        SetVal("lvl_black", Param(path!, RgbLevelsOp.Type, "black"));
        SetVal("lvl_gamma", lvlP != null ? Param(path!, RgbLevelsOp.Type, "gamma") : 1);
        SetVal("lvl_white", lvlP != null ? Param(path!, RgbLevelsOp.Type, "white") : 1);
        // Per-channel: lấy từ params nếu có, ngược lại về mặc định identity (0/1/1).
        SetVal("lvl_blackR", LvlCh(lvlP, "blackR", 0)); SetVal("lvl_whiteR", LvlCh(lvlP, "whiteR", 1)); SetVal("lvl_gammaR", LvlCh(lvlP, "gammaR", 1));
        SetVal("lvl_blackG", LvlCh(lvlP, "blackG", 0)); SetVal("lvl_whiteG", LvlCh(lvlP, "whiteG", 1)); SetVal("lvl_gammaG", LvlCh(lvlP, "gammaG", 1));
        SetVal("lvl_blackB", LvlCh(lvlP, "blackB", 0)); SetVal("lvl_whiteB", LvlCh(lvlP, "whiteB", 1)); SetVal("lvl_gammaB", LvlCh(lvlP, "gammaB", 1));
        SetVal("hlrecon", Param(path!, HighlightReconstructionOp.Type, "amount"));
        SetVal("cbr_liftHue", Param(path!, ColorBalanceRgbOp.Type, "liftHue"));
        SetVal("cbr_liftSat", Param(path!, ColorBalanceRgbOp.Type, "liftSat"));
        SetVal("cbr_gammaHue", Param(path!, ColorBalanceRgbOp.Type, "gammaHue"));
        SetVal("cbr_gammaSat", Param(path!, ColorBalanceRgbOp.Type, "gammaSat"));
        SetVal("cbr_gainHue", Param(path!, ColorBalanceRgbOp.Type, "gainHue"));
        SetVal("cbr_gainSat", Param(path!, ColorBalanceRgbOp.Type, "gainSat"));
        SetVal("cbr_chroma", Param(path!, ColorBalanceRgbOp.Type, "chroma"));
        SetVal("cbr_contrast", Param(path!, ColorBalanceRgbOp.Type, "contrast"));
        SetVal("cc_ga", Param(path!, ColorContrastOp.Type, "greenMagenta"));
        SetVal("cc_by", Param(path!, ColorContrastOp.Type, "blueYellow"));
        SetVal("clarity", Param(path!, ClarityOp.Type, "amount"));
        SetVal("texture", Param(path!, TextureOp.Type, "amount"));
        SetVal("sharpen", Param(path!, SharpenOp.Type, "amount"));
        var sharpP = FindOp(path!, SharpenOp.Type);
        SetVal("sharpenRadius", sharpP != null ? Param(path!, SharpenOp.Type, "radius") : 1.0);
        SetVal("sharpenMask", Param(path!, SharpenOp.Type, "masking"));
        SetVal("lumaNR", Param(path!, LumaNoiseReductionOp.Type, "amount"));
        SetVal("colorNR", Param(path!, ColorNoiseReductionOp.Type, "amount"));
        SetVal("chromaNR", Param(path!, ChromaDenoiseOp.Type, "amount"));
        SetVal("diffuse", Param(path!, DiffuseOp.Type, "amount"));
        SetVal("defrPurple", Param(path!, DefringeOp.Type, "purple"));
        SetVal("defrGreen", Param(path!, DefringeOp.Type, "green"));
        SetVal("hotpix", Param(path!, HotPixelOp.Type, "strength"));
        SetVal("hotpixThr", FindOp(path!, HotPixelOp.Type) != null ? Param(path!, HotPixelOp.Type, "threshold") : 0.5);
        SetVal("caRed", Param(path!, CaCorrectOp.Type, "red"));
        SetVal("caBlue", Param(path!, CaCorrectOp.Type, "blue"));
        SetVal("aiDenoise", Param(path!, AiDenoiseOp.Type, "strength"));
        if (_chkAiUpscale != null) _chkAiUpscale.IsChecked = FindOp(path!, AiUpscaleOp.Type) != null;
        SetVal("vignette", Param(path!, VignetteOp.Type, "amount"));
        SetVal("grain", Param(path!, GrainOp.Type, "amount"));
        SetVal("glow", Param(path!, GlowOp.Type, "amount"));

        SetVal("pc_hi", Param(path!, ParametricCurveOp.Type, "hi"));
        SetVal("pc_lt", Param(path!, ParametricCurveOp.Type, "lt"));
        SetVal("pc_dk", Param(path!, ParametricCurveOp.Type, "dk"));
        SetVal("pc_sh", Param(path!, ParametricCurveOp.Type, "sh"));

        SetVal("st_hiHue", Param(path!, SplitToningOp.Type, "hiHue"));
        SetVal("st_hiSat", Param(path!, SplitToningOp.Type, "hiSat"));
        SetVal("st_shHue", Param(path!, SplitToningOp.Type, "shHue"));
        SetVal("st_shSat", Param(path!, SplitToningOp.Type, "shSat"));
        SetVal("st_bal", Param(path!, SplitToningOp.Type, "balance"));

        SetVal("straighten", Param(path!, CropOp.Type, "angle"));

        // Crop rectangle (đọc lại để overlay vẽ đúng).
        var cropP = FindOp(path!, CropOp.Type);
        if (cropP != null)
        {
            _cropX = (float)Param(path!, CropOp.Type, "x");
            _cropY = (float)Param(path!, CropOp.Type, "y");
            float cw = (float)Param(path!, CropOp.Type, "w");
            float ch = (float)Param(path!, CropOp.Type, "h");
            _cropW = cw > 0 ? cw : 1f;
            _cropH = ch > 0 ? ch : 1f;
        }
        else { _cropX = 0; _cropY = 0; _cropW = 1f; _cropH = 1f; }
        CropChanged?.Invoke(this, (_cropX, _cropY, _cropW, _cropH));

        // Perspective / Upright
        var perspP = FindOp(path!, PerspectiveOp.Type);
        SetVal("persp_v", Param(path!, PerspectiveOp.Type, "vert"));
        SetVal("persp_h", Param(path!, PerspectiveOp.Type, "horiz"));
        SetVal("persp_scale", perspP != null ? Param(path!, PerspectiveOp.Type, "scale") : 1);

        SetVal("lens_k1", Param(path!, LensCorrectionOp.Type, "k1"));
        SetVal("lens_k2", Param(path!, LensCorrectionOp.Type, "k2"));
        SetVal("lens_vig", Param(path!, LensCorrectionOp.Type, "vig"));

        // Color Unify
        SetVal("uni_hue", Param(path!, ColorUnifyOp.Type, "hue"));
        var uniSat = FindOp(path!, ColorUnifyOp.Type);
        SetVal("uni_sat", uniSat != null ? Param(path!, ColorUnifyOp.Type, "sat") : 0.5);
        SetVal("uni_int", Param(path!, ColorUnifyOp.Type, "intensity"));

        // WB Kelvin
        var kelvinP = FindOp(path!, WhiteBalanceKelvinOp.Type);
        SetVal("kelvin", kelvinP != null ? Param(path!, WhiteBalanceKelvinOp.Type, "kelvin") : 6500);

        // Selective color
        SetVal("sel_src", Param(path!, SelectiveColorOp.Type, "src"));
        SetVal("sel_tgt", Param(path!, SelectiveColorOp.Type, "tgt"));
        var selTol = Param(path!, SelectiveColorOp.Type, "tol");
        SetVal("sel_tol", selTol > 0 ? selTol : 30);
        SetVal("sel_str", Param(path!, SelectiveColorOp.Type, "strength"));

        // 3D LUT
        var lutP = FindOp(path!, LutCubeOp.Type);
        _lutPath = lutP != null && lutP.TryGetValue("path", out var lp) ? lp : "";
        if (_lutLabel != null)
            _lutLabel.Text = string.IsNullOrEmpty(_lutPath) ? "(chưa chọn LUT)" : System.IO.Path.GetFileName(_lutPath);
        var lutInt = Param(path!, LutCubeOp.Type, "intensity");
        SetVal("lut_intensity", lutP != null ? lutInt : 1);

        var hsl = FindOp(path!, HslMixerOp.Type) is { } hp ? HslMixerOp.FromParams(hp) : null;
        for (int i = 0; i < HslMixerOp.Bands; i++)
        {
            _hslHue[i] = hsl?.Hue[i] ?? 0;
            _hslSat[i] = hsl?.Sat[i] ?? 0;
            _hslLum[i] = hsl?.Lum[i] ?? 0;
        }
        LoadBandIntoSliders();

        // Tone curve editor (2.2)
        var curveP = FindOp(path!, ToneCurveOp.Type);
        _curveData[0] = curveP != null && curveP.TryGetValue("rgb", out var crgb) && !string.IsNullOrEmpty(crgb) ? crgb : "0,0;1,1";
        _curveData[1] = curveP != null && curveP.TryGetValue("r", out var cr) && !string.IsNullOrEmpty(cr) ? cr : "0,0;1,1";
        _curveData[2] = curveP != null && curveP.TryGetValue("g", out var cg) && !string.IsNullOrEmpty(cg) ? cg : "0,0;1,1";
        _curveData[3] = curveP != null && curveP.TryGetValue("b", out var cb) && !string.IsNullOrEmpty(cb) ? cb : "0,0;1,1";
        if (_curveEditor != null && _curveChannel != null)
            _curveEditor.SetPoints(_curveData[_curveChannel.SelectedIndex < 0 ? 0 : _curveChannel.SelectedIndex]);
        if (_chkCurvePreserveHue != null)
            _chkCurvePreserveHue.IsChecked = curveP != null && curveP.TryGetValue("preserveHue", out var cph) && cph == "true";

        // Color grading 3-way (3.3)
        var grade = FindOp(path!, ColorGradingOp.Type) is { } gp ? ColorGradingOp.FromParams(gp) : null;
        for (int i = 0; i < 4; i++)
        {
            _gradeHue[i] = grade?.Hue[i] ?? 0;
            _gradeSat[i] = grade?.Sat[i] ?? 0;
            _gradeWheels[i]?.SetValue(_gradeHue[i], _gradeSat[i]);
        }
        SetVal("cg_sh_lum", grade?.Lum[0] ?? 0);
        SetVal("cg_mid_lum", grade?.Lum[1] ?? 0);
        SetVal("cg_hi_lum", grade?.Lum[2] ?? 0);
        SetVal("cg_blend", grade?.Blending ?? 0.5f);

        // Black & White (8b)
        var bwP = FindOp(path!, BlackWhiteOp.Type);
        if (_chkBw != null) _chkBw.IsChecked = bwP != null && bwP.TryGetValue("enabled", out var bwe) && bwe == "true";
        SetVal("bw_r", bwP != null ? Param(path!, BlackWhiteOp.Type, "wr") : 0.299);
        SetVal("bw_g", bwP != null ? Param(path!, BlackWhiteOp.Type, "wg") : 0.587);
        SetVal("bw_b", bwP != null ? Param(path!, BlackWhiteOp.Type, "wb") : 0.114);
        SetVal("bw_toneHue", Param(path!, BlackWhiteOp.Type, "toneHue"));
        SetVal("bw_toneStr", Param(path!, BlackWhiteOp.Type, "toneStr"));

        // Invert (8c)
        var invP = FindOp(path!, InvertOp.Type);
        if (_chkInvert != null) _chkInvert.IsChecked = invP != null && invP.TryGetValue("enabled", out var ive) && ive == "true";

        // Film Negative (negadoctor)
        var filmP = FindOp(path!, FilmNegativeOp.Type);
        if (_chkFilmNeg != null) _chkFilmNeg.IsChecked = filmP != null && filmP.TryGetValue("enabled", out var fve) && fve == "true";
        SetVal("film_rbase", filmP != null ? Param(path!, FilmNegativeOp.Type, "rbase") : 0.50);
        SetVal("film_gbase", filmP != null ? Param(path!, FilmNegativeOp.Type, "gbase") : 0.30);
        SetVal("film_bbase", filmP != null ? Param(path!, FilmNegativeOp.Type, "bbase") : 0.18);
        SetVal("film_gamma", filmP != null ? Param(path!, FilmNegativeOp.Type, "gamma") : 1);
        SetVal("film_exposure", filmP != null ? Param(path!, FilmNegativeOp.Type, "exposure") : 1);

        // Input profile (D2.2)
        if (_cmbInputProfile != null)
        {
            var ipP = FindOp(path!, InputProfileOp.Type);
            ColorSpaces.TryParse(ipP != null && ipP.TryGetValue("space", out var sp) ? sp : "sRGB", out var ipSpace);
            _cmbInputProfile.SelectedIndex = (int)ipSpace;
        }

        // Auto WB gains (ChannelGain)
        var gainP = FindOp(path!, ChannelGainOp.Type);
        _wbGainR = gainP != null ? (float)Param(path!, ChannelGainOp.Type, "r") : 1f;
        _wbGainG = gainP != null ? (float)Param(path!, ChannelGainOp.Type, "g") : 1f;
        _wbGainB = gainP != null ? (float)Param(path!, ChannelGainOp.Type, "b") : 1f;
        if (_wbGainR <= 0f) _wbGainR = 1f;
        if (_wbGainG <= 0f) _wbGainG = 1f;
        if (_wbGainB <= 0f) _wbGainB = 1f;

        // Local adjustment masks (6.4/6.7)
        LoadMasks(path!);
        LoadHealing(path!);
        LoadLiquify(path!);
        _loading = false;
        RefreshHistogram();
    }

    private IReadOnlyDictionary<string, string>? FindOp(string path, string opType)
    {
        if (_history == null) return null;
        var stack = _history.GetStack(path);
        int pointer = _history.GetPointer(path);
        for (int i = Math.Min(pointer, stack.Count) - 1; i >= 0; i--)
            if (string.Equals(stack[i].OpType, opType, StringComparison.OrdinalIgnoreCase))
                return stack[i].Params;
        return null;
    }

    private double Param(string path, string opType, string key)
    {
        var p = FindOp(path, opType);
        if (p != null && p.TryGetValue(key, out var s) &&
            double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return v;
        return 0;
    }

    private void LoadBandIntoSliders()
    {
        SetVal("hsl_hue", _hslHue[_band]);
        SetVal("hsl_sat", _hslSat[_band]);
        SetVal("hsl_lum", _hslLum[_band]);
    }

    private void SyncSlidersToBand()
    {
        _hslHue[_band] = (float)GetVal("hsl_hue");
        _hslSat[_band] = (float)GetVal("hsl_sat");
        _hslLum[_band] = (float)GetVal("hsl_lum");
    }

    private void BandCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _bandCombo == null) return;
        SyncSlidersToBand();
        _band = _bandCombo.SelectedIndex < 0 ? 0 : _bandCombo.SelectedIndex;
        _loading = true;
        LoadBandIntoSliders();
        _loading = false;
    }

    // ===== Tone Curve editor (2.2) =====
    private void CurveChannel_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_curveEditor == null || _curveChannel == null) return;
        int ch = _curveChannel.SelectedIndex < 0 ? 0 : _curveChannel.SelectedIndex;
        _curveEditor.SetPoints(_curveData[ch]);
    }

    private void CurveEditor_Changed(object? sender, string serialized)
    {
        if (_loading || _curveChannel == null) return;
        int ch = _curveChannel.SelectedIndex < 0 ? 0 : _curveChannel.SelectedIndex;
        _curveData[ch] = serialized;
        ScheduleCommit();
    }

    /// <summary>Gom toàn bộ slider thành chuỗi op canonical và đẩy vào history (atomic group).</summary>
    private void Commit()
    {
        if (_currentPath == null || _history == null) return;
        SyncSlidersToBand();
        var ops = BuildOps();
        _history.UpsertGroup(_currentPath, "Develop", ops);
        RefreshHistogram();
    }

    /// <summary>Dựng danh sách op theo thứ tự xử lý chuẩn (geometry trước, hiệu ứng sau).</summary>
    private List<EditOperation> BuildOps()
    {
        var ops = new List<EditOperation>();

        // 0) Geometry (Crop/Straighten) — trước tiên để op màu áp lên ảnh đã cắt.
        float angle = (float)GetVal("straighten");
        var crop = new CropOp { X = _cropX, Y = _cropY, W = _cropW, H = _cropH, Angle = angle };
        if (!crop.IsIdentity)
            ops.Add(Op(CropOp.Type, "Crop / Straighten", crop.ToParams()));

        // Orientation đã lưu riêng qua nút xoay (đọc lại để giữ).
        var orient = FindOp(_currentPath!, OrientationOp.Type);
        if (orient != null) ops.Add(Op(OrientationOp.Type, "Orientation", new Dictionary<string, string>(orient)));

        // 0a) Perspective / Upright (sau crop/orientation, trước op màu).
        var persp = new PerspectiveOp
        {
            Vertical = (float)GetVal("persp_v"), Horizontal = (float)GetVal("persp_h"),
            Scale = (float)GetVal("persp_scale"),
        };
        if (!persp.IsIdentity) ops.Add(Op(PerspectiveOp.Type, "Perspective", persp.ToParams()));

        // 0a1) Liquify / Warp (sau perspective, trước lens) — khớp DevelopModules.PipelineOrder.
        AppendLiquifyOp(ops);

        // 0a2) Lens correction (distortion + vignette) — sau perspective, trước op màu.
        var lens = new LensCorrectionOp
        {
            K1 = (float)GetVal("lens_k1"), K2 = (float)GetVal("lens_k2"),
            VignetteCorrection = (float)GetVal("lens_vig"),
        };
        if (!lens.IsIdentity) ops.Add(Op(LensCorrectionOp.Type, "Lens Correction", lens.ToParams()));

        // 0c) Healing/Clone (sau geometry để toạ độ chấm khớp ảnh đã cắt/sửa méo).
        AppendHealingOp(ops);

        // 0c2) Input color profile (D2.2) — quy ảnh về working sRGB trước mọi op màu.
        if (_cmbInputProfile?.SelectedItem is ComboBoxItem ipItem &&
            ColorSpaces.TryParse(ipItem.Content?.ToString(), out var ipSpace) &&
            ipSpace != ColorSpaces.Space.Srgb)
        {
            var ip = new InputProfileOp { Source = ipSpace };
            ops.Add(Op(InputProfileOp.Type, "Input Profile", ip.ToParams()));
        }

        // 0c1) Film Negative (negadoctor) — sau input profile, trước WB/màu.
        var filmNeg = new FilmNegativeOp
        {
            Enabled = _chkFilmNeg?.IsChecked == true,
            RBase = (float)GetVal("film_rbase"), GBase = (float)GetVal("film_gbase"), BBase = (float)GetVal("film_bbase"),
            Gamma = (float)GetVal("film_gamma"), Exposure = (float)GetVal("film_exposure"),
        };
        if (!filmNeg.IsIdentity) ops.Add(Op(FilmNegativeOp.Type, "Film Negative", filmNeg.ToParams()));

        // 0b) White balance Kelvin (trước Basic).
        var wbk = new WhiteBalanceKelvinOp { Kelvin = (float)GetVal("kelvin"), Tint = 0f };
        if (!wbk.IsIdentity) ops.Add(Op(WhiteBalanceKelvinOp.Type, "WB (Kelvin)", wbk.ToParams()));

        // 0c) Auto White Balance gains (ChannelGain) — sau WB Kelvin, trước Basic.
        var wbGain = new ChannelGainOp { R = _wbGainR, G = _wbGainG, B = _wbGainB };
        if (!wbGain.IsIdentity) ops.Add(Op(ChannelGainOp.Type, "Auto WB", wbGain.ToParams()));

        // 1) Basic
        var basic = new DevelopBasicOp
        {
            Temp = (float)GetVal("temp"), Tint = (float)GetVal("tint"),
            Exposure = (float)GetVal("exposure"), Contrast = (float)GetVal("contrast"),
            Highlights = (float)GetVal("highlights"), Shadows = (float)GetVal("shadows"),
            Whites = (float)GetVal("whites"), Blacks = (float)GetVal("blacks"),
            Vibrance = (float)GetVal("vibrance"), Saturation = (float)GetVal("saturation"),
        };
        if (!basic.IsIdentity) ops.Add(Op(DevelopBasicOp.Type, "Basic", basic.ToParams()));

        // 2) Parametric curve
        var pc = new ParametricCurveOp
        {
            Highlights = (float)GetVal("pc_hi"), Lights = (float)GetVal("pc_lt"),
            Darks = (float)GetVal("pc_dk"), Shadows = (float)GetVal("pc_sh"),
        };
        if (!pc.IsIdentity) ops.Add(Op(ParametricCurveOp.Type, "Parametric Curve", pc.ToParams()));

        // 2b) Tone curve (point editor)
        var curveParams = new Dictionary<string, string>
        {
            ["rgb"] = _curveData[0], ["r"] = _curveData[1], ["g"] = _curveData[2], ["b"] = _curveData[3],
            ["preserveHue"] = _chkCurvePreserveHue?.IsChecked == true ? "true" : "false",
        };
        var curveOp = ToneCurveOp.FromParams(curveParams);
        if (!curveOp.IsIdentity) ops.Add(Op(ToneCurveOp.Type, "Tone Curve", curveParams));

        // 3) Dehaze
        var dehaze = new DehazeOp { Amount = (float)GetVal("dehaze") };
        if (!dehaze.IsIdentity) ops.Add(Op(DehazeOp.Type, "Dehaze", dehaze.ToParams()));

        // 4) Filmic
        var filmic = new FilmicOp { Amount = (float)GetVal("filmic") };
        if (!filmic.IsIdentity) ops.Add(Op(FilmicOp.Type, "Filmic", filmic.ToParams()));

        // 4b) Tone Mapping nâng cao (D1): Tone Equalizer -> Sigmoid -> Filmic RGB.
        var toneEq = new ToneEqualizerOp
        {
            Blacks = (float)GetVal("teq_blacks"), Shadows = (float)GetVal("teq_shadows"),
            Midtones = (float)GetVal("teq_mid"), Highlights = (float)GetVal("teq_highlights"),
            Whites = (float)GetVal("teq_whites"),
        };
        if (!toneEq.IsIdentity) ops.Add(Op(ToneEqualizerOp.Type, "Tone Equalizer", toneEq.ToParams()));

        var sigmoid = new SigmoidOp { Amount = (float)GetVal("sig_amt"), Contrast = (float)GetVal("sig_contrast") };
        if (!sigmoid.IsIdentity) ops.Add(Op(SigmoidOp.Type, "Sigmoid", sigmoid.ToParams()));

        var filmRgb = new FilmicRgbOp
        {
            Amount = (float)GetVal("filmrgb_amt"), WhiteRelative = (float)GetVal("filmrgb_white"),
            BlackRelative = (float)GetVal("filmrgb_black"), Contrast = (float)GetVal("filmrgb_contrast"),
            Latitude = (float)GetVal("filmrgb_lat"), Saturation = (float)GetVal("filmrgb_sat"),
        };
        if (!filmRgb.IsIdentity) ops.Add(Op(FilmicRgbOp.Type, "Filmic RGB", filmRgb.ToParams()));

        // 4c) Levels (D2.5) — master + per-channel R/G/B (chỉ ghi kênh khác identity).
        var levels = new RgbLevelsOp
        {
            Black = (float)GetVal("lvl_black"), Gamma = (float)GetVal("lvl_gamma"), White = (float)GetVal("lvl_white"),
            BlackR = ChVal("lvl_blackR", 0), WhiteR = ChVal("lvl_whiteR", 1), GammaR = ChVal("lvl_gammaR", 1),
            BlackG = ChVal("lvl_blackG", 0), WhiteG = ChVal("lvl_whiteG", 1), GammaG = ChVal("lvl_gammaG", 1),
            BlackB = ChVal("lvl_blackB", 0), WhiteB = ChVal("lvl_whiteB", 1), GammaB = ChVal("lvl_gammaB", 1),
        };
        if (!levels.IsIdentity) ops.Add(Op(RgbLevelsOp.Type, "Levels", levels.ToParams()));

        // 4d) Highlight reconstruction (D5.3)
        var hlr = new HighlightReconstructionOp { Amount = (float)GetVal("hlrecon") };
        if (!hlr.IsIdentity) ops.Add(Op(HighlightReconstructionOp.Type, "Highlight Recon", hlr.ToParams()));

        // 5) HSL
        var hsl = new HslMixerOp();
        Array.Copy(_hslHue, hsl.Hue, HslMixerOp.Bands);
        Array.Copy(_hslSat, hsl.Sat, HslMixerOp.Bands);
        Array.Copy(_hslLum, hsl.Lum, HslMixerOp.Bands);
        if (!hsl.IsIdentity) ops.Add(Op(HslMixerOp.Type, "HSL / Color Mixer", hsl.ToParams()));

        // 5b) Color Balance RGB 4-way (D2.1)
        var cbr = new ColorBalanceRgbOp
        {
            LiftHue = (float)GetVal("cbr_liftHue"), LiftSat = (float)GetVal("cbr_liftSat"),
            GammaHue = (float)GetVal("cbr_gammaHue"), GammaSat = (float)GetVal("cbr_gammaSat"),
            GainHue = (float)GetVal("cbr_gainHue"), GainSat = (float)GetVal("cbr_gainSat"),
            GlobalChroma = (float)GetVal("cbr_chroma"), GlobalContrast = (float)GetVal("cbr_contrast"),
        };
        if (!cbr.IsIdentity) ops.Add(Op(ColorBalanceRgbOp.Type, "Color Balance RGB", cbr.ToParams()));

        // 5c) Color Contrast Lab (D2.4)
        var cc = new ColorContrastOp { GreenMagenta = (float)GetVal("cc_ga"), BlueYellow = (float)GetVal("cc_by") };
        if (!cc.IsIdentity) ops.Add(Op(ColorContrastOp.Type, "Color Contrast", cc.ToParams()));

        // 5d) Velvia (D2.3)
        var velvia = new VelviaOp { Amount = (float)GetVal("velvia") };
        if (!velvia.IsIdentity) ops.Add(Op(VelviaOp.Type, "Velvia", velvia.ToParams()));

        // 6) Split toning
        var st = new SplitToningOp
        {
            HiHue = (float)GetVal("st_hiHue"), HiSat = (float)GetVal("st_hiSat"),
            ShHue = (float)GetVal("st_shHue"), ShSat = (float)GetVal("st_shSat"),
            Balance = (float)GetVal("st_bal"),
        };
        if (!st.IsIdentity) ops.Add(Op(SplitToningOp.Type, "Split Toning", st.ToParams()));

        // 6a) Color Grading 3-way (3.3)
        var grade = new ColorGradingOp { Blending = (float)GetVal("cg_blend") };
        Array.Copy(_gradeHue, grade.Hue, 4);
        Array.Copy(_gradeSat, grade.Sat, 4);
        grade.Lum[0] = (float)GetVal("cg_sh_lum");
        grade.Lum[1] = (float)GetVal("cg_mid_lum");
        grade.Lum[2] = (float)GetVal("cg_hi_lum");
        if (!grade.IsIdentity) ops.Add(Op(ColorGradingOp.Type, "Color Grading", grade.ToParams()));

        // 6b) Selective color
        var sel = new SelectiveColorOp
        {
            SourceHue = (float)GetVal("sel_src"), TargetHue = (float)GetVal("sel_tgt"),
            Tolerance = (float)GetVal("sel_tol"), Strength = (float)GetVal("sel_str"),
        };
        if (!sel.IsIdentity) ops.Add(Op(SelectiveColorOp.Type, "Selective Color", sel.ToParams()));

        // 6b2) Color Unify
        var uni = new ColorUnifyOp
        {
            TargetHue = (float)GetVal("uni_hue"), TargetSat = (float)GetVal("uni_sat"),
            Intensity = (float)GetVal("uni_int"),
        };
        if (!uni.IsIdentity) ops.Add(Op(ColorUnifyOp.Type, "Color Unify", uni.ToParams()));

        // 6c) 3D LUT
        if (!string.IsNullOrEmpty(_lutPath))
        {
            var lut = new LutCubeOp { Path = _lutPath, Intensity = (float)GetVal("lut_intensity") };
            ops.Add(Op(LutCubeOp.Type, "3D LUT", lut.ToParams()));
        }

        // 7) Detail
        var colorNR = new ColorNoiseReductionOp { Amount = (float)GetVal("colorNR") };
        if (!colorNR.IsIdentity) ops.Add(Op(ColorNoiseReductionOp.Type, "Color NR", colorNR.ToParams()));
        var lumaNR = new LumaNoiseReductionOp { Amount = (float)GetVal("lumaNR") };
        if (!lumaNR.IsIdentity) ops.Add(Op(LumaNoiseReductionOp.Type, "Luminance NR", lumaNR.ToParams()));
        var chromaNR = new ChromaDenoiseOp { Amount = (float)GetVal("chromaNR") };
        if (!chromaNR.IsIdentity) ops.Add(Op(ChromaDenoiseOp.Type, "Chroma NR", chromaNR.ToParams()));
        var hotpix = new HotPixelOp { Strength = (float)GetVal("hotpix"), Threshold = (float)GetVal("hotpixThr") };
        if (!hotpix.IsIdentity) ops.Add(Op(HotPixelOp.Type, "Hot Pixel", hotpix.ToParams()));
        var ca = new CaCorrectOp { Red = (float)GetVal("caRed"), Blue = (float)GetVal("caBlue") };
        if (!ca.IsIdentity) ops.Add(Op(CaCorrectOp.Type, "CA Correct", ca.ToParams()));
        var defr = new DefringeOp { Purple = (float)GetVal("defrPurple"), Green = (float)GetVal("defrGreen") };
        if (!defr.IsIdentity) ops.Add(Op(DefringeOp.Type, "Defringe", defr.ToParams()));
        var clarity = new ClarityOp { Amount = (float)GetVal("clarity") };
        if (!clarity.IsIdentity) ops.Add(Op(ClarityOp.Type, "Clarity", clarity.ToParams()));
        var texture = new TextureOp { Amount = (float)GetVal("texture") };
        if (!texture.IsIdentity) ops.Add(Op(TextureOp.Type, "Texture", texture.ToParams()));
        var sharpen = new SharpenOp { Amount = (float)GetVal("sharpen"), Radius = (float)GetVal("sharpenRadius"), Masking = (float)GetVal("sharpenMask") };
        if (!sharpen.IsIdentity) ops.Add(Op(SharpenOp.Type, "Sharpen", sharpen.ToParams()));
        var diffuse = new DiffuseOp { Amount = (float)GetVal("diffuse") };
        if (!diffuse.IsIdentity) ops.Add(Op(DiffuseOp.Type, "Diffuse/Sharpen", diffuse.ToParams()));

        // 8) Effects
        var vig = new VignetteOp { Amount = (float)GetVal("vignette") };
        if (!vig.IsIdentity) ops.Add(Op(VignetteOp.Type, "Vignette", vig.ToParams()));
        var grain = new GrainOp { Amount = (float)GetVal("grain") };
        if (!grain.IsIdentity) ops.Add(Op(GrainOp.Type, "Grain", grain.ToParams()));
        var glow = new GlowOp { Amount = (float)GetVal("glow") };
        if (!glow.IsIdentity) ops.Add(Op(GlowOp.Type, "Glow / Soften", glow.ToParams()));

        // 8b) Black & White (sau màu, trước local). Chuyển xám + nhuộm.
        var bw = new BlackWhiteOp
        {
            Enabled = _chkBw?.IsChecked == true,
            RedWeight = (float)GetVal("bw_r"), GreenWeight = (float)GetVal("bw_g"), BlueWeight = (float)GetVal("bw_b"),
            ToneHue = (float)GetVal("bw_toneHue"), ToneStrength = (float)GetVal("bw_toneStr"),
        };
        if (!bw.IsIdentity) ops.Add(Op(BlackWhiteOp.Type, "Black & White", bw.ToParams()));

        // 8c) Invert (negative) — cuối cùng trước local.
        var inv = new InvertOp { Enabled = _chkInvert?.IsChecked == true };
        if (!inv.IsIdentity) ops.Add(Op(InvertOp.Type, "Negative / Invert", inv.ToParams()));

        // 8d) AI Denoise (4.3) — op cuối, chỉ chạy full-res (export) qua AiOpHost.
        var aiDn = new AiDenoiseOp { Strength = (float)GetVal("aiDenoise") };
        if (!aiDn.IsIdentity) ops.Add(Op(AiDenoiseOp.Type, "AI Denoise", aiDn.ToParams()));

        // 9) Local adjustments (masked ops) — sau cùng để áp lên kết quả global.
        AppendMaskOps(ops);

        // 10) AI Upscale (#7) — op resizing CUỐI cùng, chỉ chạy full-res khi export.
        if (_chkAiUpscale?.IsChecked == true)
            ops.Add(Op(AiUpscaleOp.Type, "AI Upscale", new AiUpscaleOp { Factor = 4 }.ToParams()));

        return ops;
    }

    private static EditOperation Op(string type, string title, Dictionary<string, string> p)
        => new() { PluginId = "Develop", OpType = type, Title = title, Params = p };

    // ===== Geometry buttons =====
    private void RotateBy(int dir)
    {
        if (_currentPath == null || _history == null) return;
        var cur = FindOp(_currentPath, OrientationOp.Type) is { } p ? OrientationOp.FromParams(p) : new OrientationOp();
        cur.Rotate90 = ((cur.Rotate90 + dir) % 4 + 4) % 4;
        ApplyOrientation(cur);
    }

    /// <summary>Xoay ảnh đang chọn (dir=-1 trái, +1 phải). Gọi được từ CenterPreview mode bar.</summary>
    public void RotateActive(int dir) => RotateBy(dir);

    /// <summary>Lật ảnh đang chọn (true=ngang, false=dọc). Gọi từ CenterPreview.</summary>
    public void FlipActive(bool horizontal) => ToggleFlip(horizontal);

    private void ToggleFlip(bool horizontal)
    {
        if (_currentPath == null || _history == null) return;
        var cur = FindOp(_currentPath, OrientationOp.Type) is { } p ? OrientationOp.FromParams(p) : new OrientationOp();
        if (horizontal) cur.FlipH = !cur.FlipH; else cur.FlipV = !cur.FlipV;
        ApplyOrientation(cur);
    }

    private void ApplyOrientation(OrientationOp orient)
    {
        var ops = BuildOps();
        ops.RemoveAll(o => o.OpType == OrientationOp.Type);
        if (!orient.IsIdentity)
            ops.Insert(0, Op(OrientationOp.Type, "Orientation", orient.ToParams()));
        _history!.UpsertGroup(_currentPath!, "Develop", ops);
    }

    private void SetEnabled(bool on)
    {
        panelSliders.IsEnabled = on;
        panelSliders.Opacity = on ? 1.0 : 0.4;
        txtNoImage.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        btnReset.IsEnabled = on;
        btnAuto.IsEnabled = on;
        btnCopy.IsEnabled = on;
    }

    private void BtnReset_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPath == null || _history == null) return;
        _loading = true;
        foreach (var kv in _defaults) SetVal(kv.Key, kv.Value);
        Array.Clear(_hslHue); Array.Clear(_hslSat); Array.Clear(_hslLum);
        // reset tone curve + color grading
        for (int i = 0; i < 4; i++) _curveData[i] = "0,0;1,1";
        if (_curveEditor != null) _curveEditor.SetPoints("0,0;1,1");
        if (_chkCurvePreserveHue != null) _chkCurvePreserveHue.IsChecked = false;
        Array.Clear(_gradeHue); Array.Clear(_gradeSat);
        for (int i = 0; i < 4; i++) _gradeWheels[i]?.SetValue(0, 0);
        // reset B&W / Invert / Auto WB
        if (_chkBw != null) _chkBw.IsChecked = false;
        if (_chkInvert != null) _chkInvert.IsChecked = false;
        if (_chkFilmNeg != null) _chkFilmNeg.IsChecked = false;
        if (_chkAiUpscale != null) _chkAiUpscale.IsChecked = false;
        if (_cmbInputProfile != null) _cmbInputProfile.SelectedIndex = 0;
        _wbGainR = 1f; _wbGainG = 1f; _wbGainB = 1f;
        ClearMasks();
        ClearHealing();
        ClearLiquify();
        _loading = false;
        _history.UpsertGroup(_currentPath, "Develop", Array.Empty<EditOperation>());
    }

    private void BtnAuto_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPath == null || _renderer == null) return;
        var sug = _renderer.AnalyzeAuto(_currentPath);
        if (sug == null) return;
        var v = sug.Value;
        _loading = true;
        SetVal("exposure", v.Exposure);
        SetVal("contrast", v.Contrast);
        SetVal("whites", v.Whites);
        SetVal("blacks", v.Blacks);
        SetVal("shadows", v.Shadows);
        SetVal("highlights", v.Highlights);
        _loading = false;
        Commit();
    }

    /// <summary>Auto Levels (D2.5): chọn điểm đen/trắng theo phân vị histogram rồi nạp vào slider Levels.</summary>
    private void BtnAutoLevels_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPath == null || _renderer == null) return;
        var sug = _renderer.AnalyzeAutoLevels(_currentPath);
        if (sug == null) return;
        var v = sug.Value;
        _loading = true;
        SetVal("lvl_black", v.Black);
        SetVal("lvl_white", v.White);
        SetVal("lvl_gamma", v.Gamma);
        _loading = false;
        Commit();
    }

    private void BtnCopy_Click(object sender, RoutedEventArgs e)
    {
        if (_clipboard == null || _history == null || _currentPath == null) return;
        _clipboard.Copy(_history, _currentPath);
    }

    /// <summary>Auto White Balance (13.2): phân tích gray-world rồi áp qua ChannelGainOp.</summary>
    private void BtnAutoWb_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPath == null || _renderer == null) return;
        var g = _renderer.AnalyzeAutoWhiteBalance(_currentPath);
        if (g == null) return;
        _wbGainR = g.Value.R; _wbGainG = g.Value.G; _wbGainB = g.Value.B;
        Commit();
    }

    /// <summary>Bắn khi user bật eyedropper WB; CenterPreview vào chế độ click chọn điểm.</summary>
    public event EventHandler? WhiteBalancePickRequested;

    private void BtnPickWb_Click(object sender, RoutedEventArgs e)
        => WhiteBalancePickRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>CenterPreview gọi lại khi user click 1 điểm (toạ độ chuẩn hoá) để lấy mẫu WB.</summary>
    public void ApplyWhiteBalancePick(float nx, float ny)
    {
        if (_currentPath == null || _renderer == null) return;
        var g = _renderer.SampleWhiteBalance(_currentPath, nx, ny);
        if (g == null) return;
        _wbGainR = g.Value.R; _wbGainG = g.Value.G; _wbGainB = g.Value.B;
        Commit();
    }

    /// <summary>Bắn khi user bật eyedropper Film Base; CenterPreview vào chế độ click chọn mép phim.</summary>
    public event EventHandler? FilmBasePickRequested;

    private void BtnPickFilmBase_Click(object sender, RoutedEventArgs e)
        => FilmBasePickRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>CenterPreview gọi lại khi user click mép phim -> nạp màu base + bật Film Negative.</summary>
    public void ApplyFilmBasePick(float nx, float ny)
    {
        if (_currentPath == null || _renderer == null) return;
        var b = _renderer.SampleFilmBase(_currentPath, nx, ny);
        if (b == null) return;
        _loading = true;
        SetVal("film_rbase", b.Value.R);
        SetVal("film_gbase", b.Value.G);
        SetVal("film_bbase", b.Value.B);
        if (_chkFilmNeg != null) _chkFilmNeg.IsChecked = true;
        _loading = false;
        Commit();
    }

    private void BtnLutPick_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Chọn 3D LUT (.cube)",
            Filter = "Cube LUT (*.cube)|*.cube|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true) return;
        _lutPath = dlg.FileName;
        if (_lutLabel != null) _lutLabel.Text = System.IO.Path.GetFileName(_lutPath);
        Commit();
    }

    private void BtnPaste_Click(object sender, RoutedEventArgs e)
    {
        if (_clipboard == null || _history == null || !_clipboard.HasCopied) return;
        var targets = _workspace != null && _workspace.Selection.Count > 0
            ? _workspace.Selection.ToList()
            : (_currentPath != null ? new List<string> { _currentPath } : new List<string>());
        if (targets.Count == 0) return;
        _clipboard.PasteToMany(_history, targets);
        LoadFor(_currentPath);
    }

    // ===== Develop Presets (dùng IStyleService) =====
    private void RefreshPresetList()
    {
        if (cmbPreset == null) return;
        _loading = true;
        cmbPreset.Items.Clear();
        cmbPreset.Items.Add(new ComboBoxItem { Content = "(chọn preset)", Tag = null });
        cmbPreset.SelectedIndex = 0;
        if (_styles != null)
        {
            try
            {
                foreach (var s in _styles.Styles)
                    cmbPreset.Items.Add(new ComboBoxItem { Content = s.Name, Tag = s.Id });
            }
            catch { }
        }
        _loading = false;
    }

    private void BtnSavePreset_Click(object sender, RoutedEventArgs e)
    {
        if (_styles == null || _currentPath == null || _history == null) return;
        // Đảm bảo history phản ánh slider hiện tại trước khi snapshot.
        Commit();
        var dlg = new PresetNameDialog { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.PresetName)) return;
        try
        {
            _styles.SaveFromHistory(dlg.PresetName.Trim(), _currentPath);
            RefreshPresetList();
        }
        catch { }
    }

    private void CmbPreset_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _styles == null || _history == null || _currentPath == null) return;
        if (cmbPreset.SelectedItem is not ComboBoxItem item || item.Tag is not string id) return;
        try
        {
            var style = _styles.Styles.FirstOrDefault(s => s.Id == id);
            if (style == null) return;
            // Áp preset: thay nhóm Develop bằng op Develop của preset (atomic, clone params).
            var ops = style.Operations
                .Where(o => string.Equals(o.PluginId, "Develop", StringComparison.OrdinalIgnoreCase))
                .Select(o => new EditOperation
                {
                    PluginId = "Develop", OpType = o.OpType, Title = o.Title,
                    Params = new Dictionary<string, string>(o.Params)
                }).ToList();
            _history.UpsertGroup(_currentPath, "Develop", ops);
            LoadFor(_currentPath);
        }
        catch { }
    }
}
