using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace ImageTool.Plugins.VisionTagger;

public partial class VisionTaggerControl : UserControl
{
    public ObservableCollection<string> Tags { get; set; } = new ObservableCollection<string>();

    public VisionTaggerControl()
    {
        InitializeComponent();
        lstTags.ItemsSource = Tags;
    }

    private void BtnLoadImage_Click(object sender, RoutedEventArgs e)
    {
        var openFileDialog = new OpenFileDialog
        {
            Filter = "Image files (*.jpg, *.jpeg, *.png, *.webp)|*.jpg;*.jpeg;*.png;*.webp|All files (*.*)|*.*"
        };
        
        if (openFileDialog.ShowDialog() == true)
        {
            try
            {
                var bitmap = new BitmapImage(new Uri(openFileDialog.FileName));
                imgPreview.Source = bitmap;
                tbPlaceholder.Visibility = Visibility.Collapsed;
                
                // Reset state
                txtDescription.Text = "";
                Tags.Clear();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi tải ảnh: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private async void BtnAnalyze_Click(object sender, RoutedEventArgs e)
    {
        if (imgPreview.Source == null)
        {
            MessageBox.Show("Vui lòng tải một tấm ảnh lên trước!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        btnAnalyze.IsEnabled = false;
        pnlLoading.Visibility = Visibility.Visible;
        txtDescription.Text = "Đang kết nối AI Vision...";
        Tags.Clear();

        try
        {
            // TODO: Implement logic liên lạc tới AI Core (Onnx/Python/CloudAPI)
            await System.Threading.Tasks.Task.Delay(2000); // Simulate network/infer delay
            
            // Mock result
            txtDescription.Text = "Một chiếc ô tô màu đỏ đang đậu trong bãi giữ xe dưới trời mây tuyệt đẹp phong cách cyberpunk.";
            Tags.Add("car");
            Tags.Add("red");
            Tags.Add("parking");
            Tags.Add("cyberpunk");
            Tags.Add("clouds");
            Tags.Add("beautiful lighting");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Lỗi phân tích: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            txtDescription.Text = "";
        }
        finally
        {
            pnlLoading.Visibility = Visibility.Collapsed;
            btnAnalyze.IsEnabled = true;
        }
    }

    private void BtnCopyDesc_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(txtDescription.Text))
            Clipboard.SetText(txtDescription.Text);
    }

    private void BtnCopyTags_Click(object sender, RoutedEventArgs e)
    {
        if (Tags.Count > 0)
        {
            var combinedTags = string.Join(", ", Tags);
            Clipboard.SetText(combinedTags);
        }
    }
}
