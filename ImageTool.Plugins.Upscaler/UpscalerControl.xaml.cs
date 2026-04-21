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
using System.Collections.ObjectModel;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;

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
        CmbModel_SelectionChanged(null, null);

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

    private void CmbModel_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (cmbScale == null || cmbModel == null) return;
        cmbScale.Items.Clear();
        
        var selectedItem = cmbModel.SelectedItem as ComboBoxItem;
        string selContent = selectedItem?.Content?.ToString() ?? "";
        
        if (selContent.Contains("AuraSR"))
        {
            cmbScale.Items.Add(new ComboBoxItem { Content = "Auto (API)", Tag = "24", IsSelected = true });
        }
        else 
        {
            cmbScale.Items.Add(new ComboBoxItem { Content = "16 MP", Tag = "16" });
            cmbScale.Items.Add(new ComboBoxItem { Content = "21 MP", Tag = "21" });
            cmbScale.Items.Add(new ComboBoxItem { Content = "24 MP", Tag = "24", IsSelected = true });
            cmbScale.Items.Add(new ComboBoxItem { Content = "36 MP", Tag = "36" });
        }
    }

    private bool _isDraggingSplitter = false;
    private double _splitPercent = 0.5;
    
    private System.Windows.Point _lastPanPosition;
    private bool _isPanning = false;

    private void GridImageContainer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateSplitClip();
    }

    private void GridImageContainer_MouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (imgOriginal.Source == null && imgPreview.Source == null) return;
        _isPanning = true;
        _lastPanPosition = e.GetPosition(borderImageArea);
        gridImageContainer.CaptureMouse();
    }

    private void GridImageContainer_MouseRightButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_isPanning)
        {
            _isPanning = false;
            if (!_isDraggingSplitter) gridImageContainer.ReleaseMouseCapture();
        }
    }

    private void GridImageContainer_MouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (imgOriginal.Source == null && imgPreview.Source == null) return;

        double zoomChange = e.Delta > 0 ? 1.15 : 1 / 1.15;
        var newScale = Math.Clamp(imgScaleTransform.ScaleX * zoomChange, 1.0, 20.0);

        var mousePos = e.GetPosition(borderImageArea);
        
        // Cố định tâm phóng to vào vị trí con trỏ chuột
        imgTranslateTransform.X = mousePos.X - (mousePos.X - imgTranslateTransform.X) * (newScale / imgScaleTransform.ScaleX);
        imgTranslateTransform.Y = mousePos.Y - (mousePos.Y - imgTranslateTransform.Y) * (newScale / imgScaleTransform.ScaleY);

        imgScaleTransform.ScaleX = newScale;
        imgScaleTransform.ScaleY = newScale;
    }

    private void GridImageContainer_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control))
        {
            if (imgOriginal.Source != null || imgPreview.Source != null)
            {
                _isPanning = true;
                _lastPanPosition = e.GetPosition(borderImageArea);
                gridImageContainer.CaptureMouse();
                e.Handled = true;
                return;
            }
        }

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
        if (_isPanning)
        {
            var curPos = e.GetPosition(borderImageArea);
            imgTranslateTransform.X += curPos.X - _lastPanPosition.X;
            imgTranslateTransform.Y += curPos.Y - _lastPanPosition.Y;
            _lastPanPosition = curPos;
        }

        if (_isDraggingSplitter)
        {
            UpdateSplitPosition(e.GetPosition(gridImageContainer));
        }
    }

    private void GridImageContainer_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_isPanning)
        {
            _isPanning = false;
            if (!_isDraggingSplitter) gridImageContainer.ReleaseMouseCapture();
            return;
        }

        if (_isDraggingSplitter)
        {
            _isDraggingSplitter = false;
            if (!_isPanning) gridImageContainer.ReleaseMouseCapture();
        }
    }

    private void GridImageContainer_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isDraggingSplitter || _isPanning)
        {
            _isDraggingSplitter = false;
            _isPanning = false;
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
        if (!string.IsNullOrEmpty(_currentImagePath)) return;

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
        if (!string.IsNullOrEmpty(_currentImagePath)) return;

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
            
            imgScaleTransform.ScaleX = 1.0;
            imgScaleTransform.ScaleY = 1.0;
            imgTranslateTransform.X = 0;
            imgTranslateTransform.Y = 0;

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

            int targetMp = 24;
            if (cmbScale.SelectedItem is ComboBoxItem scaleItem && int.TryParse(scaleItem.Tag?.ToString(), out int parsedScale))
            {
                targetMp = parsedScale;
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
            
            var selectedItem = cmbModel.SelectedItem as ComboBoxItem;
            string selContent = selectedItem?.Content?.ToString() ?? "";
            
            if (selContent.Contains("Fast Resize"))
            {
                // Fast Resize Interpolation (No AI)
                resultData = await ProcessFastResizeAsync(_currentImagePath, targetMp, progress, ct);
            }
            else if (selContent.Contains("AuraSR"))
            {
                resultData = await ProcessAuraSRAsync(_currentImagePath, ct);
            }
            else
            {
                string mdFileName = selectedItem?.Tag?.ToString();
                if (string.IsNullOrEmpty(mdFileName)) throw new Exception("ComboBox Model chưa cấu hình Tag chứa tên file ONNX!");
                
                resultData = await ProcessOnnxAsync(_currentImagePath, targetDeviceId, perfMode, targetMp, mdFileName, progress, ct);
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

    private async Task<(byte[] ImageBytes, string SavedPath)> ProcessOnnxAsync(string imagePath, int targetDeviceId, PerformanceMode perfMode, int targetMp, string mdFileName, IProgress<int> progress, CancellationToken ct)
    {
        return await Task.Run(() => 
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var mdPath = Path.Combine(baseDir, "Plugins", "ImageTool.Plugins.Upscaler", "Models", mdFileName);
            
            if (!File.Exists(mdPath))
            {
                // Fallback debug mode
                mdPath = Path.Combine(baseDir, "Models", mdFileName);
                if (!File.Exists(mdPath)) throw new Exception($"Không tìm thấy file Model tại: {mdPath}\nVui lòng copy file .onnx (và .data nếu có) vào thư mục Models.");
            }
            
            ct.ThrowIfCancellationRequested();
            
            var outputDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Output");
            if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);
            
            string randId = Guid.NewGuid().ToString("N").Substring(0, 6);
            string originalName = Path.GetFileNameWithoutExtension(imagePath);
            string savePath = Path.Combine(outputDir, $"{originalName}_{targetMp}MP_{randId}.png");

            using var image = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(imagePath);
            var upscaler = new OnnxUpscaler(mdPath, targetDeviceId, perfMode);
            var resultSharp = upscaler.Process(image, progress, targetMp, ct);

            
            ct.ThrowIfCancellationRequested();
            
            resultSharp.SaveAsPng(savePath);
            byte[] outBytes = File.ReadAllBytes(savePath);
            return (ImageBytes: outBytes, SavedPath: savePath);
        }, ct);
    }

    private async Task<(byte[] ImageBytes, string SavedPath)> ProcessFastResizeAsync(string imagePath, int targetMp, IProgress<int> progress, CancellationToken ct)
    {
        return await Task.Run(() => 
        {
            ct.ThrowIfCancellationRequested();
            progress.Report(10);
            
            var outputDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Output");
            if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);
            
            string randId = Guid.NewGuid().ToString("N").Substring(0, 6);
            string originalName = Path.GetFileNameWithoutExtension(imagePath);
            string savePath = Path.Combine(outputDir, $"{originalName}_{targetMp}MP_Lanczos_{randId}.png");

            using var image = SixLabors.ImageSharp.Image.Load(imagePath);
            progress.Report(30);

            long currentPixels = (long)image.Width * image.Height;
            long targetPixels = targetMp * 1000000L;
            
            if (currentPixels >= targetPixels)
            {
                // Nếu ảnh đã to hơn mức chọn thì không resize
                image.SaveAsPng(savePath);
            }
            else
            {
                double scaleFactor = Math.Sqrt((double)targetPixels / currentPixels);
                int newWidth = (int)(image.Width * scaleFactor);
                int newHeight = (int)(image.Height * scaleFactor);
                
                progress.Report(50);
                
                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Size = new SixLabors.ImageSharp.Size(newWidth, newHeight),
                    Sampler = KnownResamplers.Lanczos3
                }));
                
                progress.Report(80);
                image.SaveAsPng(savePath);
            }
            
            progress.Report(100);
            ct.ThrowIfCancellationRequested();
            
            byte[] outBytes = File.ReadAllBytes(savePath);
            return (ImageBytes: outBytes, SavedPath: savePath);
        }, ct);
    }
}
