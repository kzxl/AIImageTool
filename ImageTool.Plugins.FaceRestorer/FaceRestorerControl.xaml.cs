using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ImageTool.Core;
using SixLabors.ImageSharp;

namespace ImageTool.Plugins.FaceRestorer;

public partial class FaceRestorerControl : UserControl
{
    private IWorkspaceService? _workspace;
    private IModelDownloader? _downloader;
    private IImageToolHost? _host;
    private GpenProcessor? _processor;

    // Ảnh nguồn = ảnh đang mở ở Center Preview.
    private string? CurrentImagePath => _host?.ActiveImagePath ?? _workspace?.ActiveImage;

    public FaceRestorerControl()
    {
        InitializeComponent();
    }

    public void AttachServices(IServiceProvider sp)
    {
        _workspace = sp.GetService(typeof(IWorkspaceService)) as IWorkspaceService;
        _downloader = sp.GetService(typeof(IModelDownloader)) as IModelDownloader;
        _host = sp.GetService(typeof(IImageToolHost)) as IImageToolHost;

        if (_host != null)
        {
            _host.ActiveImageChanged += (s, path) => Dispatcher.BeginInvoke(() => OnActiveImageChanged(path));
            OnActiveImageChanged(_host.ActiveImagePath);
        }
    }

    private void OnActiveImageChanged(string? path)
    {
        bool has = !string.IsNullOrEmpty(path) && File.Exists(path);
        txtActiveImage.Text = has ? Path.GetFileName(path) : "(chưa chọn ảnh)";
        btnProcess.IsEnabled = has;
    }

    private async void BtnProcess_Click(object sender, RoutedEventArgs e)
    {
        var capturedPath = CurrentImagePath;
        if (string.IsNullOrEmpty(capturedPath) || !File.Exists(capturedPath))
        {
            MessageBox.Show("Hãy chọn ảnh ở khung xem trung tâm trước.", "Face Restorer", MessageBoxButton.OK, MessageBoxImage.Information);
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
                _host?.ReportProgress(percent, "Face Restore");
            });

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

            _host?.ShowResult(result);
            _host?.ReportProgress(-1);
            txtStatus.Text = $"Hoàn tất, đã lưu: {Path.GetFileName(result)}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Lỗi: {ex.Message}", "Face Restorer", MessageBoxButton.OK, MessageBoxImage.Error);
            txtStatus.Text = "Lỗi xử lý!";
            _host?.ReportProgress(-1);
        }
        finally
        {
            btnProcess.Content = "Restore Face";
            btnProcess.IsEnabled = true;
            pbProgress.Visibility = Visibility.Hidden;
        }
    }
}
