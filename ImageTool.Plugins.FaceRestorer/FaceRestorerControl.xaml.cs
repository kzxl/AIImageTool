using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using ImageTool.Core;
using SixLabors.ImageSharp;

namespace ImageTool.Plugins.FaceRestorer;

public partial class FaceRestorerControl : UserControl
{
    private string _currentImagePath = "";
    private IWorkspaceService? _workspace;
    private IModelDownloader? _downloader;
    private GpenProcessor? _processor;

    public FaceRestorerControl()
    {
        InitializeComponent();
    }

    public void AttachServices(IServiceProvider sp)
    {
        _workspace = sp.GetService(typeof(IWorkspaceService)) as IWorkspaceService;
        _downloader = sp.GetService(typeof(IModelDownloader)) as IModelDownloader;
        if (_workspace != null)
        {
            _workspace.ActiveImageChanged += (s, e) => Dispatcher.BeginInvoke(() => LoadImage(e.CurrentPath));
        }
    }

    private void LoadImage(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            imgPreview.Source = null;
            txtPrompt.Visibility = Visibility.Visible;
            _currentImagePath = "";
            return;
        }
        _currentImagePath = path;
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.UriSource = new Uri(path);
        bmp.DecodePixelWidth = 800;
        bmp.EndInit();
        bmp.Freeze();
        imgPreview.Source = bmp;
        txtPrompt.Visibility = Visibility.Collapsed;
    }

    private void Border_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files != null && files.Length > 0) LoadImage(files[0]);
        }
    }

    private async void BtnProcess_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentImagePath))
        {
            MessageBox.Show("Hãy chọn ảnh trước.", "Face Restorer", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (_downloader == null)
        {
            MessageBox.Show("Service chưa khởi tạo.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        btnProcess.Content = "Processing...";
        btnProcess.IsEnabled = false;
        pbProgress.Visibility = Visibility.Visible;
        pbProgress.Value = 0;

        try
        {
            txtStatus.Text = "Đang chuẩn bị model GPEN-BFR-512 (lần đầu sẽ tải ~284MB)...";
            var modelPath = await _downloader.EnsureAsync(KnownModels.GpenBfr512, new Progress<DownloadProgress>(p =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    txtStatus.Text = $"Đang tải model: {p.BytesReceived / (1024.0 * 1024):N1} MB ({p.Percent:N1}%)";
                    pbProgress.Value = p.Percent;
                });
            }));

            _processor ??= await Task.Run(() => new GpenProcessor(modelPath));

            var progress = new Progress<int>(percent =>
            {
                pbProgress.Value = percent;
                txtStatus.Text = $"Đang phục hồi khuôn mặt... {percent}%";
            });

            string capturedPath = _currentImagePath;
            var result = await Task.Run(() =>
            {
                using var src = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(capturedPath);
                using var restored = _processor!.Process(src, progress, CancellationToken.None);

                var outDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Output");
                Directory.CreateDirectory(outDir);
                string baseName = Path.GetFileNameWithoutExtension(capturedPath);
                string randId = Guid.NewGuid().ToString("N").Substring(0, 6);
                string savePath = Path.Combine(outDir, $"{baseName}_gpen_{randId}.png");
                restored.SaveAsPng(savePath);
                return savePath;
            });

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(result);
            bmp.EndInit();
            bmp.Freeze();
            imgPreview.Source = bmp;
            txtStatus.Text = $"Hoàn tất, đã lưu: {Path.GetFileName(result)}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Lỗi: {ex.Message}", "Face Restorer", MessageBoxButton.OK, MessageBoxImage.Error);
            txtStatus.Text = "Lỗi xử lý!";
        }
        finally
        {
            btnProcess.Content = "Restore Face";
            btnProcess.IsEnabled = true;
            pbProgress.Visibility = Visibility.Hidden;
        }
    }
}
