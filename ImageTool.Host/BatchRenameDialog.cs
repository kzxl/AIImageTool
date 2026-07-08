using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ImageTool.Shared;

namespace ImageTool.Host;

/// <summary>
/// Dialog đổi tên hàng loạt (13.7). Nhập pattern token, xem trước tên mới (live), bấm Đổi tên
/// để thực thi an toàn qua <see cref="BatchRenamer"/>. Dựng UI bằng code (không cần file XAML).
/// </summary>
public sealed class BatchRenameDialog : Window
{
    private readonly IReadOnlyList<string> _paths;
    private readonly TextBox _pattern;
    private readonly TextBox _startIndex;
    private readonly ListBox _preview;

    /// <summary>True nếu đã thực thi đổi tên (caller nên refresh view).</summary>
    public bool Renamed { get; private set; }

    public BatchRenameDialog(IReadOnlyList<string> paths)
    {
        _paths = paths;
        Title = $"Batch Rename — {paths.Count} ảnh";
        Width = 460; Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = ThemeManager.GetBrush("BgPanelBrush");
        ResizeMode = ResizeMode.CanResize;

        var root = new DockPanel { Margin = new Thickness(14) };

        var top = new StackPanel { };
        top.Children.Add(Label("Pattern (token: {name} {n:000} {date} {parent} ...)"));
        _pattern = NewBox("{name}_{n:000}");
        _pattern.TextChanged += (_, _) => UpdatePreview();
        top.Children.Add(_pattern);
        top.Children.Add(Label("Bắt đầu đánh số từ"));
        _startIndex = NewBox("1");
        _startIndex.TextChanged += (_, _) => UpdatePreview();
        top.Children.Add(_startIndex);
        top.Children.Add(Label("Xem trước:"));
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var ok = new Button { Content = "Đổi tên", Width = 90, Height = 28, IsDefault = true };
        var cancel = new Button { Content = "Đóng", Width = 80, Height = 28, Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
        ok.Click += OnRename;
        cancel.Click += (_, _) => Close();
        btnRow.Children.Add(ok); btnRow.Children.Add(cancel);
        DockPanel.SetDock(btnRow, Dock.Bottom);
        root.Children.Add(btnRow);

        _preview = new ListBox
        {
            Background = ThemeManager.GetBrush("BgBaseBrush"),
            Foreground = ThemeManager.GetBrush("TextSecondaryBrush"), BorderThickness = new Thickness(0), FontSize = 11, Margin = new Thickness(0, 4, 0, 0)
        };
        root.Children.Add(_preview);

        Content = root;
        UpdatePreview();
    }

    private List<(string OldPath, string NewName)> BuildPlan()
    {
        int start = int.TryParse(_startIndex.Text, out var s) ? s : 1;
        return FileNameTokenizer.ResolveBatch(_paths, _pattern.Text, start, DateTime.Now);
    }

    private void UpdatePreview()
    {
        if (_preview == null) return;
        _preview.Items.Clear();
        foreach (var (oldPath, newName) in BuildPlan())
            _preview.Items.Add($"{System.IO.Path.GetFileName(oldPath)}  →  {newName}");
    }

    private void OnRename(object sender, RoutedEventArgs e)
    {
        var plan = BuildPlan();
        var results = BatchRenamer.Execute(plan);
        int ok = results.Count(r => r.Success);
        int fail = results.Count - ok;
        Renamed = ok > 0;
        if (fail > 0)
        {
            var firstErr = results.FirstOrDefault(r => !r.Success)?.Error ?? "";
            MessageBox.Show($"Đổi tên xong: {ok} thành công, {fail} lỗi.\n{firstErr}", "Batch Rename",
                MessageBoxButton.OK, fail == results.Count ? MessageBoxImage.Error : MessageBoxImage.Warning);
        }
        DialogResult = Renamed;
        Close();
    }

    private static TextBox NewBox(string text) => new()
    {
        Text = text,
        Background = ThemeManager.GetBrush("BgInputBrush"),
        Foreground = ThemeManager.GetBrush("TextSecondaryBrush"), BorderThickness = new Thickness(0),
        Padding = new Thickness(4, 3, 4, 3), Margin = new Thickness(0, 0, 0, 6), FontSize = 12
    };

    private static TextBlock Label(string text) => new()
    {
        Text = text, Foreground = ThemeManager.GetBrush("TextDimBrush"),
        FontSize = 11, Margin = new Thickness(0, 2, 0, 3)
    };
}
