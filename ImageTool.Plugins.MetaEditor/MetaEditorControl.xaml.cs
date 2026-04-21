using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Collections.ObjectModel;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;

namespace ImageTool.Plugins.MetaEditor;

public class ExifItem
{
    public string Name { get; set; }
    public string Value { get; set; }
}

public partial class MetaEditorControl : UserControl
{
    private string _currentImagePath;
    private ObservableCollection<ExifItem> _exifItems = new ObservableCollection<ExifItem>();

    public MetaEditorControl()
    {
        InitializeComponent();
        this.Loaded += MetaEditorControl_Loaded;
    }

    private void MetaEditorControl_Loaded(object sender, RoutedEventArgs e)
    {
        dgMeta.ItemsSource = _exifItems;
    }

    private void Border_Drop(object sender, DragEventArgs e)
    {
        if (_currentImagePath != null) return;

        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files != null && files.Length > 0)
            {
                LoadImageFile(files[0]);
            }
        }
    }

    private void Border_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_currentImagePath != null) return;

        var openFileDialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Chọn ảnh",
            Filter = "Image files (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg|All files (*.*)|*.*",
            Multiselect = false
        };

        if (openFileDialog.ShowDialog() == true)
        {
            LoadImageFile(openFileDialog.FileName);
        }
    }

    private void LoadImageFile(string file)
    {
        var ext = Path.GetExtension(file).ToLower();
        if (ext == ".png" || ext == ".jpg" || ext == ".jpeg")
        {
            _currentImagePath = file;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(file);
            bmp.CacheOption = BitmapCacheOption.OnLoad; // Mở khoá file
            bmp.EndInit();

            imgPreview.Source = bmp;
            txtPrompt.Visibility = Visibility.Collapsed;
            btnClearImage.Visibility = Visibility.Visible;
            btnSaveMeta.IsEnabled = true;

            if (this.FindName("borderImageArea") is Border borderArea) borderArea.Cursor = System.Windows.Input.Cursors.Arrow;

            LoadExifData(file);
        }
    }

    private void BtnClearImage_Click(object sender, RoutedEventArgs e)
    {
        _currentImagePath = null;
        imgPreview.Source = null;
        txtPrompt.Visibility = Visibility.Visible;
        btnClearImage.Visibility = Visibility.Collapsed;
        btnSaveMeta.IsEnabled = false;
        _exifItems.Clear();
        
        if (this.FindName("borderImageArea") is Border borderArea) borderArea.Cursor = System.Windows.Input.Cursors.Hand;
        e.Handled = true;
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
                    _exifItems.Add(new ExifItem { Name = val.Tag.ToString(), Value = val.GetValue()?.ToString() });
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
                if (item.Name == "Software")
                {
                    try { profile.SetValue(ExifTag.Software, item.Value); updated = true; } catch { }
                }
                else if (item.Name == "ImageDescription")
                {
                    try { profile.SetValue(ExifTag.ImageDescription, item.Value); updated = true; } catch { }
                }
                else if (item.Name == "Make")
                {
                    try { profile.SetValue(ExifTag.Make, item.Value); updated = true; } catch { }
                }
                else if (item.Name == "Model")
                {
                    try { profile.SetValue(ExifTag.Model, item.Value); updated = true; } catch { }
                }
                else if (item.Name == "Artist")
                {
                    try { profile.SetValue(ExifTag.Artist, item.Value); updated = true; } catch { }
                }
                else if (item.Name == "Copyright")
                {
                    try { profile.SetValue(ExifTag.Copyright, item.Value); updated = true; } catch { }
                }
            }
            // Auto add identifier if not already have
            if (!updated) profile.SetValue(ExifTag.Software, "ImageTool v1.0");
            
            image.Metadata.ExifProfile = profile;
            image.Save(_currentImagePath);
            MessageBox.Show("Đã lưu metadata thành công vào ảnh gốc!", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
            
            LoadExifData(_currentImagePath); // reload data
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Không thể lưu Metadata: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
