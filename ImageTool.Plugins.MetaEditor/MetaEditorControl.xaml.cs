using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Collections.ObjectModel;
using ImageTool.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;

namespace ImageTool.Plugins.MetaEditor;

public class ExifItem
{
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
}

public partial class MetaEditorControl : UserControl
{
    private IWorkspaceService? _workspace;
    private IImageToolHost? _host;
    private string? _currentImagePath;
    private readonly ObservableCollection<ExifItem> _exifItems = new();

    // Ảnh nguồn = ảnh đang mở ở Center Preview.
    private string? CurrentImagePath => _host?.ActiveImagePath ?? _workspace?.ActiveImage;

    public MetaEditorControl()
    {
        InitializeComponent();
        dgMeta.ItemsSource = _exifItems;
    }

    public void AttachServices(IServiceProvider sp)
    {
        _workspace = sp.GetService(typeof(IWorkspaceService)) as IWorkspaceService;
        _host = sp.GetService(typeof(IImageToolHost)) as IImageToolHost;

        if (_host != null)
        {
            _host.ActiveImageChanged += (s, path) => Dispatcher.BeginInvoke(() => OnActiveImageChanged(path));
            OnActiveImageChanged(_host.ActiveImagePath);
        }
    }

    private void OnActiveImageChanged(string? path)
    {
        _currentImagePath = path;
        bool has = !string.IsNullOrEmpty(path) && File.Exists(path);
        txtActiveImage.Text = has ? Path.GetFileName(path) : "(chưa chọn ảnh)";
        btnSaveMeta.IsEnabled = has;
        _exifItems.Clear();
        if (has) LoadExifData(path!);
    }

    private void LoadExifData(string file)
    {
        try
        {
            _exifItems.Clear();
            using var image = SixLabors.ImageSharp.Image.Load(file);
            var profile = image.Metadata.ExifProfile;
            if (profile != null)
            {
                foreach (var val in profile.Values)
                {
                    _exifItems.Add(new ExifItem { Name = val.Tag.ToString(), Value = val.GetValue()?.ToString() ?? "" });
                }
            }
            if (_exifItems.Count == 0) _exifItems.Add(new ExifItem { Name = "[Trống]", Value = "Không có thông tin EXIF" });
        }
        catch { }
    }

    private void BtnSaveMeta_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (string.IsNullOrEmpty(_currentImagePath) || !File.Exists(_currentImagePath)) return;
            using var image = SixLabors.ImageSharp.Image.Load(_currentImagePath);
            var profile = image.Metadata.ExifProfile ?? new ExifProfile();

            bool updated = false;
            foreach (var item in _exifItems)
            {
                switch (item.Name)
                {
                    case "Software": try { profile.SetValue(ExifTag.Software, item.Value); updated = true; } catch { } break;
                    case "ImageDescription": try { profile.SetValue(ExifTag.ImageDescription, item.Value); updated = true; } catch { } break;
                    case "Make": try { profile.SetValue(ExifTag.Make, item.Value); updated = true; } catch { } break;
                    case "Model": try { profile.SetValue(ExifTag.Model, item.Value); updated = true; } catch { } break;
                    case "Artist": try { profile.SetValue(ExifTag.Artist, item.Value); updated = true; } catch { } break;
                    case "Copyright": try { profile.SetValue(ExifTag.Copyright, item.Value); updated = true; } catch { } break;
                }
            }
            if (!updated) profile.SetValue(ExifTag.Software, "ImageTool v1.0");

            image.Metadata.ExifProfile = profile;
            image.Save(_currentImagePath);
            MessageBox.Show("Đã lưu metadata thành công vào ảnh gốc!", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);

            LoadExifData(_currentImagePath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Không thể lưu Metadata: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
