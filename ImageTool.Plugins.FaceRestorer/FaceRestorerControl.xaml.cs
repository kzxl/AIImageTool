using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using SixLabors.ImageSharp;

namespace ImageTool.Plugins.FaceRestorer;

public partial class FaceRestorerControl : UserControl
{
    private string _currentImagePath = "";

    public FaceRestorerControl()
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
                _currentImagePath = files[0];
                txtPrompt.Visibility = Visibility.Collapsed;
                
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(_currentImagePath);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                imgPreview.Source = bmp;
            }
        }
    }

    private async void BtnProcess_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentImagePath))
        {
            MessageBox.Show("Please drop an image first.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
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
                txtStatus.Text = $"Đang tái tạo cấu trúc khuôn mặt... {percent}%";
            });

            var resultData = await System.Threading.Tasks.Task.Run(() => 
            {
                // Gọi tới GfpganProcessor
                using var image = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(_currentImagePath);

                var processor = new GfpganProcessor(new byte[0]); // Dummy model bytes
                var resultSharp = processor.Process(image, progress);

                using var outStream = new MemoryStream();
                resultSharp.SaveAsPng(outStream);
                
                var outputDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Output");
                if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);
                
                string randId = Guid.NewGuid().ToString("N").Substring(0, 6);
                string originalName = Path.GetFileNameWithoutExtension(_currentImagePath);
                string savePath = Path.Combine(outputDir, $"{originalName}_gfpgan_{randId}.png");
                resultSharp.SaveAsPng(savePath);

                return (ImageBytes: outStream.ToArray(), SavedPath: savePath);
            });

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = new MemoryStream(resultData.ImageBytes);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();

            imgPreview.Source = bmp; 
            txtStatus.Text = $"Hoàn tất! Đã xuất file ra Output.";

            MessageBox.Show($"Phục hồi khuôn mặt thành công!\nĐã lưu tại:\n{resultData.SavedPath}", "Tuyệt vời", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
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
