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

        bool useGpu = chkUseGpu.IsChecked == true;
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

            // Tách Thread để UI không bị đơ giật trong lúc Model đang chạy!
            var resultData = await System.Threading.Tasks.Task.Run(() => 
            {
                // 1. Tự động tìm tên Embedded Resource Model an toàn
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                var resourceName = assembly.GetManifestResourceNames().FirstOrDefault(n => n.Contains("UltraSharpV2"));
                if (string.IsNullOrEmpty(resourceName)) throw new Exception("Không tìm thấy Model nhúng trong dll!");
                
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null) throw new Exception("Không bắt được luồng Model nhúng!");
                
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                var modelBytes = ms.ToArray();

                // 2. Load ảnh vào ImageSharp Memory
                using var image = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(_currentImagePath);

                // 3. Xử lý Upscale qua luồng Tensor (hỗ trợ băm nhỏ)
                var upscaler = new OnnxUpscaler(modelBytes, useGpu);
                var resultSharp = upscaler.Process(image, progress, targetScale);

                // 4. Save ra Temp stream để đẩy lên giao diện WPF
                using var outStream = new MemoryStream();
                resultSharp.SaveAsPng(outStream);
                
                // 5. Lưu tự động ra thư mục Output
                var outputDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Output");
                if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);
                
                string randId = Guid.NewGuid().ToString("N").Substring(0, 6);
                string originalName = Path.GetFileNameWithoutExtension(_currentImagePath);
                string savePath = Path.Combine(outputDir, $"{originalName}_x4_{randId}.png");
                resultSharp.SaveAsPng(savePath);

                // Trả về tuple để UI Render
                return (ImageBytes: outStream.ToArray(), SavedPath: savePath);
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

