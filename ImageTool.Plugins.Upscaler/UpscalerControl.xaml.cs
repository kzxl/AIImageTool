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
using ImageTool.Core;
using Microsoft.Extensions.DependencyInjection;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;

namespace ImageTool.Plugins.Upscaler;

public partial class UpscalerControl : UserControl
{
    private static readonly HttpClient _sharedHttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    private CancellationTokenSource? _cancellationTokenSource;

    private IBatchService? _batch;
    private IWorkspaceService? _workspace;
    private IImageToolHost? _host;

    // Ảnh nguồn = ảnh đang mở ở Center Preview (workspace), không còn picker riêng.
    private string? _currentImagePath => _host?.ActiveImagePath ?? _workspace?.ActiveImage;

    public UpscalerControl()
    {
        InitializeComponent();
        this.Loaded += UpscalerControl_Loaded;
    }

    public void AttachServices(IServiceProvider sp)
    {
        _batch = sp.GetService<IBatchService>();
        _workspace = sp.GetService<IWorkspaceService>();
        _host = sp.GetService<IImageToolHost>();

        if (_host != null)
        {
            _host.ActiveImageChanged += (s, path) =>
                Dispatcher.BeginInvoke(() => OnActiveImageChanged(path));
            OnActiveImageChanged(_host.ActiveImagePath);
        }

        if (_workspace != null)
        {
            _workspace.SelectionChanged += (s, e) =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    int n = e.Selection.Count;
                    btnBatch.Content = n > 1 ? $"Batch ({n})" : "Batch";
                    btnBatch.IsEnabled = n > 0 && _batch != null;
                });
            };
        }
    }

    private void OnActiveImageChanged(string? path)
    {
        bool has = !string.IsNullOrEmpty(path) && File.Exists(path);
        txtActiveImage.Text = has ? Path.GetFileName(path) : "(chưa chọn ảnh)";
        btnProcess.IsEnabled = has;
    }

    private void BtnBatch_Click(object sender, RoutedEventArgs e)
    {
        if (_workspace == null || _batch == null) return;
        var sel = _workspace.Selection.ToList();
        if (sel.Count == 0)
        {
            MessageBox.Show("Hãy chọn ảnh trong browser trước (Ctrl+click multi-select).", "Batch", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Đọc thông số hiện tại từ UI
        int targetDeviceId = -1;
        if (cmbDevice.SelectedItem is GpuInfo selectedGpu) targetDeviceId = selectedGpu.DeviceId;

        var perfMode = (cmbPerformance.SelectedItem is ComboBoxItem perfItem && perfItem.Tag?.ToString() == "Unleashed")
            ? "Unleashed" : "Safe";

        int targetMp = 24;
        if (cmbScale.SelectedItem is ComboBoxItem scaleItem && int.TryParse(scaleItem.Tag?.ToString(), out int parsedScale)) targetMp = parsedScale;

        var modelItem = cmbModel.SelectedItem as ComboBoxItem;
        string modelFile = modelItem?.Tag?.ToString() ?? ""; // "" = Lanczos fallback

        var jobs = sel.Select(p => new BatchJob
        {
            PluginId = UpscalerBatchAdapter.Plugin,
            OpType = UpscalerBatchAdapter.OpUpscale,
            InputPath = p,
            Params = new System.Collections.Generic.Dictionary<string, string>
            {
                ["model"] = modelFile,
                ["device"] = targetDeviceId.ToString(),
                ["targetMp"] = targetMp.ToString(),
                ["perf"] = perfMode
            }
        }).ToList();

        _batch.EnqueueRange(jobs);
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

    private void CmbModel_SelectionChanged(object? sender, SelectionChangedEventArgs? e)
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
                MessageBox.Show("Vui lòng chọn một ảnh ở khung xem trung tâm để Upscale!", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            string srcPath = _currentImagePath!;

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

            (byte[]? ImageBytes, string? SavedPath) resultData = (null, null);
            
            var selectedItem = cmbModel.SelectedItem as ComboBoxItem;
            string selContent = selectedItem?.Content?.ToString() ?? "";
            
            if (selContent.Contains("Fast Resize"))
            {
                // Fast Resize Interpolation (No AI)
                resultData = await ProcessFastResizeAsync(srcPath, targetMp, progress, ct);
            }
            else if (selContent.Contains("AuraSR"))
            {
                resultData = await ProcessAuraSRAsync(srcPath, ct);
            }
            else
            {
                string? mdFileName = selectedItem?.Tag?.ToString();
                if (string.IsNullOrEmpty(mdFileName)) throw new Exception("ComboBox Model chưa cấu hình Tag chứa tên file ONNX!");
                
                resultData = await ProcessOnnxAsync(srcPath, targetDeviceId, perfMode, targetMp, mdFileName, progress, ct);
            }

            if (resultData.ImageBytes != null)
            {
                // Đẩy kết quả "after" lên Center Preview (splitter so sánh do host quản lý).
                _host?.ShowResult(resultData.SavedPath, resultData.ImageBytes);

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

    /// <summary>
    /// Ping /health của AuraSR worker với timeout ngắn. Ném exception rõ ràng nếu worker chưa chạy
    /// (lỗi kết nối) hoặc model chưa nạp xong (503), thay vì để job treo tới 3 phút.
    /// </summary>
    private async Task EnsureAuraWorkerReadyAsync(CancellationToken ct)
    {
        try
        {
            using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            probeCts.CancelAfter(TimeSpan.FromSeconds(3));
            using var req = new HttpRequestMessage(HttpMethod.Get, "http://127.0.0.1:8000/health");
            var resp = await _sharedHttpClient.SendAsync(req, probeCts.Token);
            if (resp.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
                throw new Exception("AuraSR Worker đang chạy nhưng model chưa nạp xong. Đợi vài giây rồi thử lại (lần đầu tải model có thể lâu).");
            if (!resp.IsSuccessStatusCode)
                throw new Exception($"AuraSR Worker phản hồi bất thường ({(int)resp.StatusCode}).");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // user huỷ
        }
        catch (Exception ex) when (ex is HttpRequestException || ex is OperationCanceledException || ex is TaskCanceledException)
        {
            throw new Exception(
                "Không kết nối được AuraSR Worker (http://127.0.0.1:8000). " +
                "Hãy đảm bảo Python worker đã khởi động (ImageTool.Worker.AuraSR), " +
                "hoặc chọn model ONNX/Fast Resize thay thế.");
        }
    }

    private async Task<(byte[] ImageBytes, string SavedPath)> ProcessAuraSRAsync(string imagePath, CancellationToken ct)
    {
        pbProgress.IsIndeterminate = true;
        txtStatus.Text = "Đang kiểm tra AuraSR Worker...";

        // Health-check trước (timeout ngắn) để không treo 3 phút khi worker chưa chạy / model chưa nạp.
        await EnsureAuraWorkerReadyAsync(ct);

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
        string? jobId = doc.RootElement.GetProperty("job_id").GetString();
        if (string.IsNullOrEmpty(jobId)) throw new Exception("Không bóc tách được Thẻ Chờ từ Python!");

        int pingCount = 1;
        int maxRetries = 60; // Tối đa 60 * 3s = 180s = 3 phút
        byte[]? targetBytes = null;
        
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
            var mdPath = ModelLocator.Resolve(mdFileName)
                ?? throw new Exception($"Không tìm thấy file Model '{mdFileName}'.\nVui lòng copy file .onnx (và .data nếu có) vào thư mục Models của plugin Upscaler.");
            
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
