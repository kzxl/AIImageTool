using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ImageTool.Core;

namespace ImageTool.Host.Workspace;

/// <summary>
/// Dialog định nghĩa rule cho Smart Collection (8.3): nhập tên + các tiêu chí lọc
/// (camera/lens/ISO/aperture/focal/date/text). Trả về <see cref="Query"/> để CatalogService
/// resolve động. Dựng UI bằng code để không cần thêm file XAML.
/// </summary>
public sealed class SmartCollectionDialog : Window
{
    private readonly TextBox _name = NewBox();
    private readonly TextBox _text = NewBox();
    private readonly TextBox _make = NewBox();
    private readonly TextBox _model = NewBox();
    private readonly TextBox _lens = NewBox();
    private readonly TextBox _isoMin = NewBox();
    private readonly TextBox _isoMax = NewBox();
    private readonly TextBox _apMin = NewBox();
    private readonly TextBox _apMax = NewBox();
    private readonly TextBox _focalMin = NewBox();
    private readonly TextBox _focalMax = NewBox();
    private readonly TextBox _keyword = NewBox();
    private readonly ComboBox _ratingMin = NewCombo("Bất kỳ", "≥ 1 ★", "≥ 2 ★", "≥ 3 ★", "≥ 4 ★", "= 5 ★");
    private readonly ComboBox _label = NewCombo("Bất kỳ", "Đỏ", "Vàng", "Xanh lá", "Xanh dương", "Tím");
    private readonly ComboBox _pick = NewCombo("Bất kỳ", "Pick", "Reject");

    public string? CollectionName { get; private set; }
    public CatalogQuery Query { get; private set; } = new();

    public SmartCollectionDialog() : this("", new CatalogQuery()) { }

    public SmartCollectionDialog(string name, CatalogQuery query)
    {
        Title = "Smart Collection — Rules";
        Width = 380;
        Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
        ResizeMode = ResizeMode.NoResize;

        _name.Text = name;
        _text.Text = query.Text ?? "";
        _make.Text = query.CameraMake ?? "";
        _model.Text = query.CameraModel ?? "";
        _lens.Text = query.LensModel ?? "";
        _isoMin.Text = query.IsoMin?.ToString(CultureInfo.InvariantCulture) ?? "";
        _isoMax.Text = query.IsoMax?.ToString(CultureInfo.InvariantCulture) ?? "";
        _apMin.Text = query.ApertureMin?.ToString(CultureInfo.InvariantCulture) ?? "";
        _apMax.Text = query.ApertureMax?.ToString(CultureInfo.InvariantCulture) ?? "";
        _focalMin.Text = query.FocalMin?.ToString(CultureInfo.InvariantCulture) ?? "";
        _focalMax.Text = query.FocalMax?.ToString(CultureInfo.InvariantCulture) ?? "";
        _keyword.Text = query.Keyword ?? "";
        _ratingMin.SelectedIndex = query.RatingMin.HasValue ? Math.Clamp(query.RatingMin.Value, 0, 5) : 0;
        _label.SelectedIndex = query.Label.HasValue ? (int)query.Label.Value : 0;
        _pick.SelectedIndex = query.Pick switch { PickFlag.Pick => 1, PickFlag.Reject => 2, _ => 0 };

        var sp = new StackPanel { Margin = new Thickness(16) };
        sp.Children.Add(Label("Tên Smart Collection"));
        sp.Children.Add(_name);
        sp.Children.Add(Label("Từ khoá (tên file / thư mục)"));
        sp.Children.Add(_text);
        sp.Children.Add(Label("Camera Make"));
        sp.Children.Add(_make);
        sp.Children.Add(Label("Camera Model"));
        sp.Children.Add(_model);
        sp.Children.Add(Label("Lens"));
        sp.Children.Add(_lens);
        sp.Children.Add(Row("ISO từ … đến", _isoMin, _isoMax));
        sp.Children.Add(Row("Khẩu độ f/ từ … đến", _apMin, _apMax));
        sp.Children.Add(Row("Tiêu cự (mm) từ … đến", _focalMin, _focalMax));
        sp.Children.Add(Label("Rating tối thiểu"));
        sp.Children.Add(_ratingMin);
        sp.Children.Add(Label("Color Label"));
        sp.Children.Add(_label);
        sp.Children.Add(Label("Pick / Reject"));
        sp.Children.Add(_pick);
        sp.Children.Add(Label("Keyword (chứa)"));
        sp.Children.Add(_keyword);

        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var ok = new Button { Content = "OK", Width = 72, Height = 28, IsDefault = true };
        var cancel = new Button { Content = "Cancel", Width = 72, Height = 28, Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
        ok.Click += OnOk;
        cancel.Click += (_, _) => DialogResult = false;
        btnPanel.Children.Add(ok);
        btnPanel.Children.Add(cancel);
        sp.Children.Add(btnPanel);

        Content = new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        CollectionName = _name.Text?.Trim();
        Query = new CatalogQuery
        {
            Text = Empty(_text.Text),
            CameraMake = Empty(_make.Text),
            CameraModel = Empty(_model.Text),
            LensModel = Empty(_lens.Text),
            IsoMin = Int(_isoMin.Text),
            IsoMax = Int(_isoMax.Text),
            ApertureMin = Dbl(_apMin.Text),
            ApertureMax = Dbl(_apMax.Text),
            FocalMin = Dbl(_focalMin.Text),
            FocalMax = Dbl(_focalMax.Text),
            RatingMin = _ratingMin.SelectedIndex > 0 ? _ratingMin.SelectedIndex : null,
            Label = _label.SelectedIndex > 0 ? (ColorLabel)_label.SelectedIndex : null,
            Pick = _pick.SelectedIndex switch { 1 => PickFlag.Pick, 2 => PickFlag.Reject, _ => (PickFlag?)null },
            Keyword = Empty(_keyword.Text),
        };
        DialogResult = true;
    }

    private static string? Empty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    private static int? Int(string? s) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;
    private static double? Dbl(string? s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static TextBox NewBox() => new()
    {
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
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 0) };
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
