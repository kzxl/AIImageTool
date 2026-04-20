using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SixLabors.ImageSharp;

namespace ImageTool.Plugins.Upscaler;

public partial class UpscalerControl : UserControl
{
    private string _currentImagePath;
    private static readonly HttpClient _sharedHttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    private CancellationTokenSource _cancellationTokenSource;

    public UpscalerControl()
    {
        InitializeComponent();
        this.Loaded += UpscalerControl_Loaded;
    }

    private async void UpscalerControl_Loaded(object sender, RoutedEventArgs e)
    {
        try 
        {
            cmbDevice.IsEnabled = false;
            var devices = await GpuDetector.GetAvailableDevicesAsync();
            cmbDevice.ItemsSource = devices;
            txtExecMode.Text = "Kiến trúc xử lý: Multi-Thread (In-Process Parallel)";

            // Mặc định chọn GPU đầu tiên nếu có (thường là Index 1 do Index 0 là CPU Only)
            if (devices.Count > 1) 
                cmbDevice.SelectedIndex = 1; 
            else 
                cmbDevice.SelectedIndex = 0; 
                
            cmbDevice.IsEnabled = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Lỗi không xác định: {ex.Message}\n{ex.StackTrace}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private bool _isDraggingSplitter = false;
    private double _splitPercent = 0.5;

    private void GridImageContainer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateSplitClip();
    }

    private void GridImageContainer_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (imgPreview.Source != null && borderSplitLine.Visibility == Visibility.Visible)
        {
            _isDraggingSplitter = true;
            gridImageContainer.CaptureMouse();
            UpdateSplitPosition(e.GetPosition(gridImageContainer));
            e.Handled = true; // Ngăn chặn sự kiện nổi bọt lên viền để không mở Dialog chọn ảnh
        }
    }

    private void GridImageContainer_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isDraggingSplitter)
        {
            UpdateSplitPosition(e.GetPosition(gridImageContainer));
        }
    }

    private void GridImageContainer_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_isDraggingSplitter)
        {
            _isDraggingSplitter = false;
            gridImageContainer.ReleaseMouseCapture();
        }
    }

    private void GridImageContainer_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isDraggingSplitter)
        {
            _isDraggingSplitter = false;
            gridImageContainer.ReleaseMouseCapture();
        }
    }

    private void UpdateSplitPosition(System.Windows.Point p)
    {
        if (imgPreview.Source == null) return;

        double width = gridImageContainer.ActualWidth;
        if (width <= 0) return;

        _splitPercent = p.X / width;
        if (_splitPercent < 0) _splitPercent = 0;
        if (_splitPercent > 1) _splitPercent = 1;

        UpdateSplitClip();
    }

    private void UpdateSplitClip()
    {
        if (imgPreview.Source == null) return;

        double width = gridImageContainer.ActualWidth;
        double height = gridImageContainer.ActualHeight;

        if (width <= 0 || height <= 0) return;

        double clipX = width * _splitPercent;

        // Clip the right side of the image
        imgPreview.Clip = new System.Windows.Media.RectangleGeometry(new Rect(clipX, 0, Math.Max(0, width - clipX), height));
        
        borderSplitLine.Margin = new Thickness(clipX, 0, 0, 0);
    }

    private void BtnClearImage_Click(object sender, RoutedEventArgs e)
    {
        _currentImagePath = null;
        imgOriginal.Source = null;
        imgPreview.Source = null;
        imgPreview.Clip = null;
        borderSplitLine.Visibility = Visibility.Collapsed;
        txtPrompt.Visibility = Visibility.Visible;
        btnClearImage.Visibility = Visibility.Collapsed;
        
        if (this.FindName("borderImageArea") is Border borderArea) borderArea.Cursor = System.Windows.Input.Cursors.Hand;
        e.Handled = true;
    }

    private void Border_Drop(object sender, DragEventArgs e)
    {
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
        var openFileDialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Chọn ảnh cần phóng to",
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
            bmp.CacheOption = BitmapCacheOption.OnLoad; // Tránh block file ảnh để có thể xoá sau khi scale
            bmp.EndInit();
            imgPreview.Source = null;
            imgPreview.Clip = null;
            borderSplitLine.Visibility = Visibility.Collapsed;

            imgOriginal.Source = bmp;
            txtPrompt.Visibility = Visibility.Collapsed;
            btnClearImage.Visibility = Visibility.Visible;

            if (this.FindName("borderImageArea") is Border borderArea) borderArea.Cursor = System.Windows.Input.Cursors.Hand;
        }
    }

    private async void BtnProcess_Click(object sender, RoutedEventArgs e)
    {
        // Nếu đang chạy thì huỷ
        if (_cancellationTokenSource != null && !_cancellationTokenSource.IsCancellationRequested)
        {
            _cancellationTokenSource.Cancel();
            return;
        }

        try
        {
            if (string.IsNullOrEmpty(_currentImagePath) || !File.Exists(_currentImagePath))
            {
                MessageBox.Show("Vui lòng chọn một ảnh để Upscale!", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int targetDeviceId = -1;
            if (cmbDevice.SelectedItem is GpuInfo selectedGpu)
            {
                targetDeviceId = selectedGpu.DeviceId;
            }

            PerformanceMode perfMode = PerformanceMode.Safe;
            if (cmbPerformance.SelectedItem is ComboBoxItem perfItem && perfItem.Tag?.ToString() == "Unleashed")
            {
                perfMode = PerformanceMode.Unleashed;
            }

            int targetScale = 4;
            if (cmbScale.SelectedItem is ComboBoxItem scaleItem && int.TryParse(scaleItem.Tag?.ToString(), out int parsedScale))
            {
                targetScale = parsedScale;
            }

            int selectedModelIndex = cmbModel.SelectedIndex;

            btnProcess.Content = "Cancel";
            pbProgress.Visibility = Visibility.Visible;
            txtStatus.Visibility = Visibility.Visible;
            pbProgress.Value = 0;
            
            _cancellationTokenSource = new CancellationTokenSource();
            var ct = _cancellationTokenSource.Token;

            var progress = new Progress<int>(percent =>
            {
                pbProgress.Value = percent;
                txtStatus.Text = $"Đang xử lý phân mảnh AI... {percent}%";
            });

            var sw = System.Diagnostics.Stopwatch.StartNew();

            (byte[] ImageBytes, string SavedPath) resultData = (null, null);
            
            if (selectedModelIndex == 1)
            {
                resultData = await ProcessAuraSRAsync(_currentImagePath, ct);
            }
            else
            {
                resultData = await ProcessOnnxAsync(_currentImagePath, targetDeviceId, perfMode, targetScale, progress, ct);
            }

            if (resultData.ImageBytes != null)
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = new MemoryStream(resultData.ImageBytes);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();

                imgPreview.Source = bmp;
                _splitPercent = 0.5;
                borderSplitLine.Visibility = Visibility.Visible;
                UpdateSplitClip();

                if (this.FindName("borderImageArea") is Border borderArea) borderArea.Cursor = System.Windows.Input.Cursors.SizeWE;

                sw.Stop();
                txtStatus.Text = $"Hoàn thành lưu tại: {resultData.SavedPath} ({sw.Elapsed.TotalSeconds:F2} giây)";
                MessageBox.Show($"Xử lý Upscale hoàn tất trong {sw.Elapsed.TotalSeconds:F2} giây!", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (OperationCanceledException)
        {
            txtStatus.Text = "Đã huỷ bởi người dùng.";
            MessageBox.Show("Tiến trình đã bị huỷ.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            txtStatus.Text = "Xảy ra lỗi!";
            string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "upscaler_error.log");
            File.AppendAllText(logPath, $"[{DateTime.Now}] Lỗi Upscale:\r\n{ex}\r\n\r\n");
            MessageBox.Show(ex.Message, "Lỗi UI/Upscale", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
            
            pbProgress.IsIndeterminate = false;
            btnProcess.Content = "Upscale";
            btnProcess.IsEnabled = true;
            pbProgress.Visibility = Visibility.Hidden;
        }
    }

    private async Task<(byte[] ImageBytes, string SavedPath)> ProcessAuraSRAsync(string imagePath, CancellationToken ct)
    {
        pbProgress.IsIndeterminate = true;
        txtStatus.Text = "Đang xin Thẻ Chờ (Job ID) từ Backend...";

        using var form = new MultipartFormDataContent();
        var fileBytes = await File.ReadAllBytesAsync(imagePath, ct);
        var imageContent = new ByteArrayContent(fileBytes);
        imageContent.Headers.ContentType = MediaTypeHeaderValue.Parse("image/png");
        form.Add(imageContent, "file", Path.GetFileName(imagePath));
        
        var response = await _sharedHttpClient.PostAsync("http://127.0.0.1:8000/upscale", form, ct);
        string initRes = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new Exception($"Lỗi gọi Python ({response.StatusCode}): {initRes}");
        
        using var doc = JsonDocument.Parse(initRes);
        string jobId = doc.RootElement.GetProperty("job_id").GetString();
        if (string.IsNullOrEmpty(jobId)) throw new Exception("Không bóc tách được Thẻ Chờ từ Python!");

        int pingCount = 1;
        int maxRetries = 60; // Tối đa 60 * 3s = 180s = 3 phút
        byte[] targetBytes = null;
        
        while (pingCount <= maxRetries)
        {
            ct.ThrowIfCancellationRequested();
            txtStatus.Text =($"Đang tính toán mạng Gen AI (Ping hỏi thăm lần {pingCount}/{maxRetries})...");
            await Task.Delay(3000, ct); 
            
            using var reqMsg = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:8000/status/{jobId}");
            var statResp = await _sharedHttpClient.SendAsync(reqMsg, HttpCompletionOption.ResponseHeadersRead, ct);
            
            if (statResp.IsSuccessStatusCode)
            {
                if (statResp.Content.Headers.ContentType?.MediaType == "image/png")
                {
                    targetBytes = await statResp.Content.ReadAsByteArrayAsync(ct);
                    break;
                }
                else
                {
                    pingCount++;
                }
            }
            else
            {
                string errStr = await statResp.Content.ReadAsStringAsync(ct);
                throw new Exception($"Lỗi hệ thống Python Worker: {errStr}");
            }
        }
        
        if (targetBytes == null)
            throw new TimeoutException("Hết thời gian chờ phản hồi từ Python Backend (> 3 phút).");
            
        pbProgress.IsIndeterminate = false;
        txtStatus.Text = "Đã tính xong! Đang nạp phân mảnh về giao diện...";
        pbProgress.Value = 85;
        
        var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Output");
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        
        string rId = Guid.NewGuid().ToString("N").Substring(0, 6);
        string oName = Path.GetFileNameWithoutExtension(imagePath);
        string dPath = Path.Combine(dir, $"{oName}_AuraSR_{rId}.png");
        
        await File.WriteAllBytesAsync(dPath, targetBytes, ct);
        pbProgress.Value = 100;
        
        return (targetBytes, dPath);
    }

    private async Task<(byte[] ImageBytes, string SavedPath)> ProcessOnnxAsync(string imagePath, int targetDeviceId, PerformanceMode perfMode, int targetScale, IProgress<int> progress, CancellationToken ct)
    {
        return await Task.Run(() => 
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            var resourceName = assembly.GetManifestResourceNames().FirstOrDefault(n => n.Contains("UltraSharpV2"));
            if (string.IsNullOrEmpty(resourceName)) throw new Exception("Không tìm thấy Model nhúng trong dll!");
            
            ct.ThrowIfCancellationRequested();
            
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null) throw new Exception("Không bắt được luồng Model nhúng!");
            
            // Có thể tối ưu truyền thẳng stream nếu thư viện ONNX hỗ trợ, ở đây giữ nguyên byte[] do logic OnnxUpscaler
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            byte[] modelBytes = ms.ToArray();
            
            ct.ThrowIfCancellationRequested();

            var outputDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Output");
            if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);
            
            string randId = Guid.NewGuid().ToString("N").Substring(0, 6);
            string originalName = Path.GetFileNameWithoutExtension(imagePath);
            string savePath = Path.Combine(outputDir, $"{originalName}_x4_{randId}.png");

            using var image = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(imagePath);
            var upscaler = new OnnxUpscaler(modelBytes, targetDeviceId, perfMode);
            var resultSharp = upscaler.Process(image, progress, targetScale, ct);
            
            ct.ThrowIfCancellationRequested();
            
            resultSharp.SaveAsPng(savePath);
            byte[] outBytes = File.ReadAllBytes(savePath);
            return (ImageBytes: outBytes, SavedPath: savePath);
        }, ct);
    }
}
