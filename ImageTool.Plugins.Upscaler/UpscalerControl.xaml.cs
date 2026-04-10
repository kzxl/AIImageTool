using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using SixLabors.ImageSharp;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace ImageTool.Plugins.Upscaler;

public partial class UpscalerControl : UserControl
{
    private string _currentImagePath;

    public UpscalerControl()
    {
        InitializeComponent();
        this.Loaded += UpscalerControl_Loaded;
    }

    private void UpscalerControl_Loaded(object sender, RoutedEventArgs e)
    {
        try 
        {
            var devices = GpuDetector.GetAvailableDevices();
            cmbDevice.ItemsSource = devices;
            txtExecMode.Text = "Kiến trúc xử lý: Multi-Thread (In-Process Parallel)";

        // Mặc định chọn GPU đầu tiên nếu có (thường là Index 1 do Index 0 là CPU Only)
        if (devices.Count > 1) 
            cmbDevice.SelectedIndex = 1; 
        else 
            cmbDevice.SelectedIndex = 0; 
        }
        catch (Exception ex)
        {
            MessageBox.Show($"[OnLoaded Error] {ex.Message}\n{ex.StackTrace}", "Internal Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
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
            imgPreview.Source = bmp;
            txtPrompt.Visibility = Visibility.Collapsed;
        }
    }

    private async void BtnProcess_Click(object sender, RoutedEventArgs e)
    {
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

            btnProcess.Content = "Processing...";
            btnProcess.IsEnabled = false;
            pbProgress.Visibility = Visibility.Visible;
            txtStatus.Visibility = Visibility.Visible;
            pbProgress.Value = 0;

            var resultData = await System.Threading.Tasks.Task.Run(async () => 
            {
                var progress = new Progress<int>(percent =>
                {
                    Dispatcher.Invoke(() => {
                        pbProgress.Value = percent;
                        txtStatus.Text = $"Đang xử lý phân mảnh AI... {percent}%";
                    });
                });

                // Luồng 2: AuraSR (Generative)
                if (selectedModelIndex == 1)
                {
                    Dispatcher.Invoke(() => {
                        pbProgress.IsIndeterminate = true;
                        txtStatus.Text = "Đang xin Thẻ Chờ (Job ID) từ Backend...";
                    });

                    using var client = new HttpClient();
                    client.Timeout = TimeSpan.FromSeconds(30); // Chỉ cần xin Job_ID nên 30s là quá đủ
                    using var form = new MultipartFormDataContent();
                    
                    var fileBytes = File.ReadAllBytes(_currentImagePath);
                    var imageContent = new ByteArrayContent(fileBytes);
                    imageContent.Headers.ContentType = MediaTypeHeaderValue.Parse("image/png");
                    form.Add(imageContent, "file", Path.GetFileName(_currentImagePath));
                    
                    // 1. Post File lên Server để xin Job ID
                    var response = await client.PostAsync("http://127.0.0.1:8000/upscale", form);
                    string initRes = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode)
                        throw new Exception($"Lỗi gọi Python ({response.StatusCode}): {initRes}");
                    
                    using var doc = JsonDocument.Parse(initRes);
                    string jobId = doc.RootElement.GetProperty("job_id").GetString();
                    if (string.IsNullOrEmpty(jobId)) throw new Exception("Không bóc tách được Thẻ Chờ từ Python!");

                    // 2. Vòng lặp Polling 3 giây/lần
                    int pingCount = 1;
                    byte[] targetBytes = null;
                    while (true)
                    {
                        Dispatcher.Invoke(() => txtStatus.Text = $"Đang tính toán mạng Gen AI (Ping hỏi thăm lần {pingCount})...");
                        await System.Threading.Tasks.Task.Delay(3000); 
                        
                        var statResp = await client.GetAsync($"http://127.0.0.1:8000/status/{jobId}");
                        if (statResp.IsSuccessStatusCode)
                        {
                            // Nếu Header rả về là hình ảnh tức là Xong
                            if (statResp.Content.Headers.ContentType?.MediaType == "image/png")
                            {
                                targetBytes = await statResp.Content.ReadAsByteArrayAsync();
                                break;
                            }
                            else
                            {
                                pingCount++;
                            }
                        }
                        else
                        {
                            string errStr = await statResp.Content.ReadAsStringAsync();
                            throw new Exception($"Lỗi hệ thống Python Worker: {errStr}");
                        }
                    }
                    
                    Dispatcher.Invoke(() => {
                        pbProgress.IsIndeterminate = false;
                        txtStatus.Text = "Đã tính xong! Đang nạp phân mảnh về giao diện...";
                        pbProgress.Value = 85;
                    });
                    
                    var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Output");
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    
                    string rId = Guid.NewGuid().ToString("N").Substring(0, 6);
                    string oName = Path.GetFileNameWithoutExtension(_currentImagePath);
                    string dPath = Path.Combine(dir, $"{oName}_AuraSR_{rId}.png");
                    
                    File.WriteAllBytes(dPath, targetBytes);
                    
                    Dispatcher.Invoke(() => pbProgress.Value = 100);
                    
                    return (ImageBytes: targetBytes, SavedPath: dPath);
                }

                // Luồng 1: ONNX (ESRGAN UltraSharp)
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                var resourceName = assembly.GetManifestResourceNames().FirstOrDefault(n => n.Contains("UltraSharpV2"));
                if (string.IsNullOrEmpty(resourceName)) throw new Exception("Không tìm thấy Model nhúng trong dll!");
                
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null) throw new Exception("Không bắt được luồng Model nhúng!");
                
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                byte[] modelBytes = ms.ToArray();
                
                var outputDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Output");
                if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);
                
                string randId = Guid.NewGuid().ToString("N").Substring(0, 6);
                string originalName = Path.GetFileNameWithoutExtension(_currentImagePath);
                string savePath = Path.Combine(outputDir, $"{originalName}_x4_{randId}.png");

                using var image = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(_currentImagePath);
                var upscaler = new OnnxUpscaler(modelBytes, targetDeviceId, perfMode);
                var resultSharp = upscaler.Process(image, progress, targetScale);
                resultSharp.SaveAsPng(savePath);

                byte[] outBytes = File.ReadAllBytes(savePath);
                return (ImageBytes: outBytes, SavedPath: savePath);
            });

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = new MemoryStream(resultData.ImageBytes);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();

            imgPreview.Source = bmp;
            pbProgress.Visibility = Visibility.Hidden;
            txtStatus.Text = $"Hoàn thành lưu tại: {resultData.SavedPath}";
            MessageBox.Show("Xử lý Upscale hoàn tất!", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            pbProgress.Visibility = Visibility.Hidden;
            txtStatus.Text = "Xảy ra lỗi!";
            string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "upscaler_error.log");
            File.AppendAllText(logPath, $"[{DateTime.Now}] Lỗi Upscale:\r\n{ex}\r\n\r\n");
            MessageBox.Show(ex.Message, "Lỗi UI/Upscale", MessageBoxButton.OK, MessageBoxImage.Error);
            txtStatus.Text = "Lỗi xử lý! Đã ghi log.";
        }
        finally
        {
            pbProgress.IsIndeterminate = false;
            btnProcess.Content = "Upscale";
            btnProcess.IsEnabled = true;
            pbProgress.Visibility = Visibility.Hidden;
        }
    }
}

