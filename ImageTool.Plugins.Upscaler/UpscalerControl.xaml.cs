using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using SixLabors.ImageSharp;

namespace ImageTool.Plugins.Upscaler;

public partial class UpscalerControl : UserControl
{
    private string _currentImagePath;

    public UpscalerControl()
    {
        InitializeComponent();
        this.Loaded += UpscalerControl_Loaded;
    }

    private bool _shouldUseMultiProcess;

    private void UpscalerControl_Loaded(object sender, RoutedEventArgs e)
    {
        var devices = GpuDetector.GetAvailableDevices();
        cmbDevice.ItemsSource = devices;
        
        int gpuCount = devices.Count - 1; // Loại trừ tùy chọn CPU Only
        if (gpuCount > 1) 
        {
            _shouldUseMultiProcess = true;
            txtExecMode.Text = "Kiến trúc tự động: Multi-Process (Cho Nhiều GPU)";
        }
        else 
        {
            _shouldUseMultiProcess = false;
            txtExecMode.Text = "Kiến trúc tự động: Multi-Thread (Đơn GPU/Nhanh)";
        }

        // Mặc định chọn GPU đầu tiên nếu có (thường là Index 1 do Index 0 là CPU Only)
        if (devices.Count > 1) 
            cmbDevice.SelectedIndex = 1; 
        else 
            cmbDevice.SelectedIndex = 0; 
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
        if (string.IsNullOrEmpty(_currentImagePath))
        {
            MessageBox.Show("Please drop an image first.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
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

        btnProcess.Content = "Processing...";
        btnProcess.IsEnabled = false;
        pbProgress.Visibility = Visibility.Visible;
        txtStatus.Visibility = Visibility.Visible;
        pbProgress.Value = 0;

        try 
        {
            var progress = new Progress<int>(percent =>
            {
                pbProgress.Value = percent;
                txtStatus.Text = $"Đang xử lý phân mảnh AI... {percent}%";
            });

            var resultData = await System.Threading.Tasks.Task.Run(() => 
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                var resourceName = assembly.GetManifestResourceNames().FirstOrDefault(n => n.Contains("UltraSharpV2"));
                if (string.IsNullOrEmpty(resourceName)) throw new Exception("Không tìm thấy Model nhúng trong dll!");
                
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null) throw new Exception("Không bắt được luồng Model nhúng!");
                
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                byte[] modelBytes = ms.ToArray();
                
                // Chuẩn bị đường dẫn Output chung
                var outputDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Output");
                if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);
                
                string randId = Guid.NewGuid().ToString("N").Substring(0, 6);
                string originalName = Path.GetFileNameWithoutExtension(_currentImagePath);
                string savePath = Path.Combine(outputDir, $"{originalName}_x4_{randId}.png");

                if (!_shouldUseMultiProcess)
                {
                    // 2A. CHẠY MULTI-THREAD TRONG CÙNG PROCESS (RẤT NHANH KHI CÓ 1 GPU HOẶC CHỈ CPU)
                    using var image = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(_currentImagePath);
                    var upscaler = new OnnxUpscaler(modelBytes, targetDeviceId, perfMode);
                    var resultSharp = upscaler.Process(image, progress, targetScale);
                    resultSharp.SaveAsPng(savePath);
                }
                else
                {
                    // 2B. CHẠY BẰNG WORKER TIẾN TRÌNH PHỤ (BẢO VỆ RAM CHỐNG CRASH KHI CÓ ĐA GPU)
                    string tempDir = Path.Combine(Path.GetTempPath(), "ImageTool_Upscaler");
                    Directory.CreateDirectory(tempDir);
                    string modelPath = Path.Combine(tempDir, "model.onnx");
                    if (!File.Exists(modelPath)) File.WriteAllBytes(modelPath, modelBytes);

                    string workerExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ImageTool.Worker.Upscaler.exe");
                if (!File.Exists(workerExe)) 
                {
                    // Nếu ở mode Debug và chạy từ Visual Studio, worker có thể nằm ở thư mục riêng
                    workerExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "ImageTool.Worker.Upscaler", "bin", "Debug", "net8.0-windows", "ImageTool.Worker.Upscaler.exe");
                    if (!File.Exists(workerExe)) throw new Exception($"Không tìm thấy file worker tại {workerExe}\nChắc chắn bạn đã biên dịch project Worker!");
                    workerExe = Path.GetFullPath(workerExe);
                }

                string modeStr = perfMode == PerformanceMode.Unleashed ? "Unleashed" : "Safe";

                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = workerExe,
                    Arguments = $"--input \"{_currentImagePath}\" --out \"{savePath}\" --scale {targetScale} --device {targetDeviceId} --mode {modeStr} --model \"{modelPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                using var process = System.Diagnostics.Process.Start(processInfo);
                if (process == null) throw new Exception("Không thể khởi động tiến trình phụ!");

                while (!process.StandardOutput.EndOfStream)
                {
                    string? line = process.StandardOutput.ReadLine();
                    if (!string.IsNullOrEmpty(line) && line.StartsWith("[PROGRESS]"))
                    {
                        var parts = line.Split(' ');
                        if (parts.Length > 1 && int.TryParse(parts[1], out int p))
                        {
                            ((IProgress<int>)progress).Report(p);
                        }
                    }
                }

                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    string err = process.StandardError.ReadToEnd();
                    throw new Exception($"Tiến trình phụ thất bại (Mã lỗi {process.ExitCode}): {err}");
                }

                }

                // 4. Trả về bytes để Render UI
                byte[] outBytes = File.ReadAllBytes(savePath);
                return (ImageBytes: outBytes, SavedPath: savePath);
            });

            // 6. Cập nhật giao diện bên Thread chính (UI Thread)
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = new MemoryStream(resultData.ImageBytes);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();

            imgPreview.Source = bmp; // Pushed kết quả cuối lên UI
            txtStatus.Text = $"Hoàn tất! Đã xuất file ra thư mục Output.";

            MessageBox.Show($"Upscale tiến trình mảng Tiled thành công!\nĐã lưu tại:\n{resultData.SavedPath}", "Quá mỹ mãn", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            // Bắt lỗi, ghi log file txt cẩn thận vì thư viện AI/Tensors hay văng StackTrace phức tạp, tránh mất
            string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "upscaler_error.log");
            File.AppendAllText(logPath, $"[{DateTime.Now}] Lỗi Upscale:\r\n{ex}\r\n\r\n");

            MessageBox.Show(ex.Message, "Lỗi UI/Upscale", MessageBoxButton.OK, MessageBoxImage.Error);
            txtStatus.Text = "Lỗi xử lý! Đã ghi log.";
        }
        finally
        {
            btnProcess.Content = "Upscale";
            btnProcess.IsEnabled = true;
            pbProgress.Visibility = Visibility.Hidden;
        }
    }
}

