using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ImageTool.Shared;

namespace ImageTool.Host.Workspace;

/// <summary>
/// Dialog cấu hình Print (in ấn): chọn khổ giấy, hướng, DPI, lề, lưới N-up, chế độ fit.
/// Trả về <see cref="PrintOptions"/> để PrintModule.Render dựng file raster sẵn-sàng-in.
/// Dựng UI bằng code (không cần file XAML), theo mẫu SmartCollectionDialog.
/// </summary>
public sealed class PhotoPrintDialog : Window
{
    private readonly ComboBox _paper = NewCombo("A4", "A3", "A5", "Letter", "Legal", "4×6 (ảnh)", "5×7 (ảnh)", "8×10 (ảnh)");
    private readonly ComboBox _orient = NewCombo("Dọc (Portrait)", "Ngang (Landscape)");
    private readonly ComboBox _dpi = NewCombo("150 DPI", "200 DPI", "300 DPI (in chất lượng)");
    private readonly ComboBox _fit = NewCombo("Fit (vừa khít, có viền)", "Fill (lấp đầy, cắt bớt)");
    private readonly TextBox _rows = NewBox("1");
    private readonly TextBox _cols = NewBox("1");
    private readonly TextBox _margin = NewBox("10");
    private readonly TextBox _gap = NewBox("5");
    private readonly CheckBox _showName;

    public PrintModule.Options Options { get; private set; } = new();

    public PhotoPrintDialog(int imageCount)
    {
        Title = $"In ấn — {imageCount} ảnh";
        Width = 360;
        Height = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
        ResizeMode = ResizeMode.NoResize;

        _paper.SelectedIndex = 0;
        _orient.SelectedIndex = 0;
        _dpi.SelectedIndex = 2;        // 300 DPI mặc định
        _fit.SelectedIndex = 0;

        // Gợi ý lưới theo số ảnh (1 ảnh -> 1x1; nhiều -> căn lưới gần vuông).
        if (imageCount > 1)
        {
            int cols = (int)Math.Ceiling(Math.Sqrt(imageCount));
            int rows = (int)Math.Ceiling((double)imageCount / cols);
            _cols.Text = cols.ToString(CultureInfo.InvariantCulture);
            _rows.Text = rows.ToString(CultureInfo.InvariantCulture);
        }

        _showName = new CheckBox
        {
            Content = "Hiện tên file dưới mỗi ảnh",
            Foreground = Brushes.Gainsboro, FontSize = 12, Margin = new Thickness(0, 4, 0, 6)
        };

        var sp = new StackPanel { Margin = new Thickness(16) };
        sp.Children.Add(Label("Khổ giấy"));
        sp.Children.Add(_paper);
        sp.Children.Add(Label("Hướng"));
        sp.Children.Add(_orient);
        sp.Children.Add(Label("Độ phân giải in"));
        sp.Children.Add(_dpi);
        sp.Children.Add(Row("Lưới: hàng × cột", _rows, _cols));
        sp.Children.Add(Row("Lề (mm) · khoảng cách (mm)", _margin, _gap));
        sp.Children.Add(Label("Cách đặt ảnh"));
        sp.Children.Add(_fit);
        sp.Children.Add(_showName);

        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var ok = new Button { Content = "Tạo file in", Width = 96, Height = 28, IsDefault = true };
        var cancel = new Button { Content = "Huỷ", Width = 72, Height = 28, Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
        ok.Click += OnOk;
        cancel.Click += (_, _) => DialogResult = false;
        btnPanel.Children.Add(ok);
        btnPanel.Children.Add(cancel);
        sp.Children.Add(btnPanel);

        Content = new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        Options = new PrintModule.Options
        {
            Paper = _paper.SelectedIndex switch
            {
                1 => PrintModule.PaperSize.A3,
                2 => PrintModule.PaperSize.A5,
                3 => PrintModule.PaperSize.Letter,
                4 => PrintModule.PaperSize.Legal,
                5 => PrintModule.PaperSize.Photo4x6,
                6 => PrintModule.PaperSize.Photo5x7,
                7 => PrintModule.PaperSize.Photo8x10,
                _ => PrintModule.PaperSize.A4
            },
            Orientation = _orient.SelectedIndex == 1 ? PrintModule.Orientation.Landscape : PrintModule.Orientation.Portrait,
            Dpi = _dpi.SelectedIndex switch { 0 => 150, 1 => 200, _ => 300 },
            Fit = _fit.SelectedIndex == 1 ? PrintModule.FitMode.Fill : PrintModule.FitMode.Fit,
            Rows = Math.Max(1, Int(_rows.Text, 1)),
            Columns = Math.Max(1, Int(_cols.Text, 1)),
            MarginMm = Math.Max(0, Dbl(_margin.Text, 10)),
            GapMm = Math.Max(0, Dbl(_gap.Text, 5)),
            ShowFileName = _showName.IsChecked == true
        };
        DialogResult = true;
    }

    private static int Int(string? s, int fallback) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;
    private static double Dbl(string? s, double fallback) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    private static TextBox NewBox(string text) => new()
    {
        Text = text,
        Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
        Foreground = Brushes.Gainsboro, BorderThickness = new Thickness(0),
        Padding = new Thickness(4, 3, 4, 3), Margin = new Thickness(0, 0, 0, 6), FontSize = 12
    };

    private static ComboBox NewCombo(params string[] items)
    {
        var cb = new ComboBox
        {
            Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
            Foreground = Brushes.Gainsboro,
            Margin = new Thickness(0, 0, 0, 6), FontSize = 12, SelectedIndex = 0
        };
        foreach (var it in items) cb.Items.Add(new ComboBoxItem { Content = it });
        return cb;
    }

    private static TextBlock Label(string text) => new()
    {
        Text = text, Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
        FontSize = 11, Margin = new Thickness(0, 2, 0, 3)
    };

    private static UIElement Row(string label, TextBox a, TextBox b)
    {
        var panel = new StackPanel();
        panel.Children.Add(Label(label));
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        a.Margin = new Thickness(0); b.Margin = new Thickness(0);
        Grid.SetColumn(a, 0); Grid.SetColumn(b, 2);
        grid.Children.Add(a);
        grid.Children.Add(b);
        panel.Children.Add(grid);
        return panel;
    }
}
