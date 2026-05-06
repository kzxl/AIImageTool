using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.ColorSpaces;
using SixLabors.ImageSharp.ColorSpaces.Conversion;
using System.Collections.Generic;

namespace ImageTool.Plugins.ColorLab;

public class ColorSwatch
{
    public string HexString { get; set; }
    public SolidColorBrush HexBrush { get; set; }
    public float Percentage { get; set; }
    public string PercentageString => $"{Percentage * 100:0.#}%";
}

public partial class ColorLabControl : UserControl
{
    private string _currentImagePath;
    private BitmapSource _currentBitmapSource;
    private SixLabors.ImageSharp.Image<Rgba32> _workingImage;

    public ColorLabControl()
    {
        InitializeComponent();
    }

    private System.Windows.Media.Color SourceColor 
    {
        get 
        {
            try { return (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(txtSource.Text); }
            catch { return System.Windows.Media.Color.FromRgb(255, 255, 255); }
        }
    }

    private System.Windows.Media.Color TargetColor 
    {
        get 
        {
            try { return (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(txtTarget.Text); }
            catch { return System.Windows.Media.Color.FromRgb(0, 0, 0); }
        }
    }

    private void Border_Drop(object sender, DragEventArgs e)
    {
        if (_currentImagePath != null) return;

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
        if (_currentImagePath != null) return;

        var openFileDialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Chọn ảnh",
            Filter = "Image files (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg|All files (*.*)|*.*",
            Multiselect = false
        };

        if (openFileDialog.ShowDialog() == true)
        {
            LoadImageFile(openFileDialog.FileName);
        }
    }

    private async void LoadImageFile(string file)
    {
        var ext = Path.GetExtension(file).ToLower();
        if (ext == ".png" || ext == ".jpg" || ext == ".jpeg")
        {
            _currentImagePath = file;
            _workingImage?.Dispose();
            _workingImage = await Task.Run(() => SixLabors.ImageSharp.Image.Load<Rgba32>(file));

            UpdatePreviewUI();
            
            pnlTools.Visibility = Visibility.Visible;
            txtPrompt.Visibility = Visibility.Collapsed;
            btnClearImage.Visibility = Visibility.Visible;
            btnSaveImage.Visibility = Visibility.Collapsed;

            if (this.FindName("borderImageArea") is Border borderArea) borderArea.Cursor = System.Windows.Input.Cursors.Arrow;

            ExtractDominantColorsAsync();
        }
    }

    private void UpdatePreviewUI()
    {
        using var ms = new MemoryStream();
        _workingImage.SaveAsPng(ms);
        ms.Position = 0;
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.StreamSource = ms;
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.EndInit();
        _currentBitmapSource = bmp;
        imgPreview.Source = _currentBitmapSource;
    }

    private void BtnClearImage_Click(object sender, RoutedEventArgs e)
    {
        _currentImagePath = null;
        imgPreview.Source = null;
        _currentBitmapSource = null;
        txtPrompt.Visibility = Visibility.Visible;
        btnClearImage.Visibility = Visibility.Collapsed;
        pnlTools.Visibility = Visibility.Collapsed;
        
        _workingImage?.Dispose();
        _workingImage = null;

        if (this.FindName("borderImageArea") is Border borderArea) borderArea.Cursor = System.Windows.Input.Cursors.Hand;
        e.Handled = true;
    }

    private async void ExtractDominantColorsAsync()
    {
        var dominantInfos = await Task.Run(() => AdvancedColorProcessor.GetKMeansColors(_workingImage, 5, 10));

        var swatches = dominantInfos.Select(info => new ColorSwatch { 
            HexString = info.Hex, 
            HexBrush = new SolidColorBrush(info.WpfColor),
            Percentage = info.Percentage
        }).ToList();

        icPalette.ItemsSource = swatches;

        if (swatches.Count > 0)
        {
            var dom = swatches[0].HexString;
            var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(dom);
            var hsl = ColorSpaceConverter.ToHsl(new Rgb(c.R/255f, c.G/255f, c.B/255f));
            
            var suggested = new List<ColorSwatch>();
            
            // 1. Analogous (Tương đồng - dịu mắt, hài hòa)
            suggested.Add(CreateSwatch(ColorSpaceConverter.ToRgb(new Hsl((hsl.H + 30) % 360, hsl.S, hsl.L))));
            suggested.Add(CreateSwatch(ColorSpaceConverter.ToRgb(new Hsl((hsl.H + 330) % 360, hsl.S, hsl.L))));
            
            // 2. Complementary (Tương phản - nổi bật)
            float compH = (hsl.H + 180) % 360;
            suggested.Add(CreateSwatch(ColorSpaceConverter.ToRgb(new Hsl(compH, hsl.S, hsl.L))));
            
            // 3. Split-Complementary (Tương phản rẽ nhánh - dễ dùng hơn Complementary)
            suggested.Add(CreateSwatch(ColorSpaceConverter.ToRgb(new Hsl((hsl.H + 150) % 360, hsl.S, hsl.L))));
            suggested.Add(CreateSwatch(ColorSpaceConverter.ToRgb(new Hsl((hsl.H + 210) % 360, hsl.S, hsl.L))));

            // 4. Triadic (Tam giác đều - cân bằng)
            suggested.Add(CreateSwatch(ColorSpaceConverter.ToRgb(new Hsl((hsl.H + 120) % 360, hsl.S, hsl.L))));
            suggested.Add(CreateSwatch(ColorSpaceConverter.ToRgb(new Hsl((hsl.H + 240) % 360, hsl.S, hsl.L))));

            icSuggested.ItemsSource = suggested;

            txtSource.Text = dom; // auto fill grading source
        }
    }

    private ColorSwatch CreateSwatch(Rgb rgb)
    {
        byte r = (byte)(rgb.R * 255);
        byte g = (byte)(rgb.G * 255);
        byte b = (byte)(rgb.B * 255);
        string hex = $"#{r:X2}{g:X2}{b:X2}";
        return new ColorSwatch { HexString = hex, HexBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b)) };
    }

    private void Swatch_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is Border b && b.DataContext is ColorSwatch swatch)
        {
            txtSource.Text = swatch.HexString;
        }
    }

    private void TxtSource_TextChanged(object sender, TextChangedEventArgs e)
    {
        try { bdrSource.Background = new SolidColorBrush(SourceColor); } catch { }
    }

    private void TxtTarget_TextChanged(object sender, TextChangedEventArgs e)
    {
        try { bdrTarget.Background = new SolidColorBrush(TargetColor); } catch { }
    }

    private void ImgPreview_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        /* Bỏ qua phần tính năng Mouse Move tạm thời vì khá phức tạp với WPF bounds */
    }

    private void ImgPreview_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // Tính năng hút màu
        if (_currentBitmapSource == null || imgPreview.Source == null) return;
        
        System.Windows.Point p = e.GetPosition(imgPreview);
        double ratioX = _currentBitmapSource.PixelWidth / imgPreview.ActualWidth;
        double ratioY = _currentBitmapSource.PixelHeight / imgPreview.ActualHeight;

        int x = (int)(p.X * ratioX);
        int y = (int)(p.Y * ratioY);

        if (x >= 0 && x < _currentBitmapSource.PixelWidth && y >= 0 && y < _currentBitmapSource.PixelHeight)
        {
            CroppedBitmap cb = new CroppedBitmap(_currentBitmapSource, new Int32Rect(x, y, 1, 1));
            byte[] pixels = new byte[4];
            cb.CopyPixels(pixels, 4, 0);

            // B G R A format for WriteableBitmap usually
            string hex = $"#{pixels[2]:X2}{pixels[1]:X2}{pixels[0]:X2}";
            txtSource.Text = hex;
        }
    }

    private async void BtnApplyColor_Click(object sender, RoutedEventArgs e)
    {
        if (_workingImage == null) return;
        
        try
        {
            btnApplyColor.IsEnabled = false;
            pnlProgress.Visibility = Visibility.Visible;

            var sourceClr = SourceColor;
            var targetClr = TargetColor;
            float tolerance = (float)slTolerance.Value;

            var sourceHsl = ColorSpaceConverter.ToHsl(new Rgb(sourceClr.R/255f, sourceClr.G/255f, sourceClr.B/255f));
            var targetHsl = ColorSpaceConverter.ToHsl(new Rgb(targetClr.R/255f, targetClr.G/255f, targetClr.B/255f));

            float sH = sourceHsl.H;
            float tH = targetHsl.H;

            await Task.Run(() => 
            {
                _workingImage.ProcessPixelRows(accessor => 
                {
                    for (int y = 0; y < accessor.Height; y++)
                    {
                        Span<Rgba32> row = accessor.GetRowSpan(y);
                        for (int x = 0; x < row.Length; x++)
                        {
                            ref Rgba32 p = ref row[x];
                            var hsl = ColorSpaceConverter.ToHsl(new Rgb(p.R/255f, p.G/255f, p.B/255f));
                            
                            // Khoảng cách Hue ngắn nhất trên vòng tròn 360 độ
                            float diff = Math.Abs(hsl.H - sH);
                            if (diff > 180) diff = 360 - diff;

                            if (diff <= tolerance)
                            {
                                // Chuyển đổi mềm (smooth)
                                float factor = 1.0f - (diff / tolerance);
                                float newH = hsl.H + ((tH - sH) * factor);
                                if (newH < 0) newH += 360;
                                if (newH > 360) newH -= 360;

                                var newRgb = ColorSpaceConverter.ToRgb(new Hsl(newH, hsl.S, hsl.L));
                                p.R = (byte)(newRgb.R * 255);
                                p.G = (byte)(newRgb.G * 255);
                                p.B = (byte)(newRgb.B * 255);
                            }
                        }
                    }
                });
            });

            UpdatePreviewUI();
            btnSaveImage.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Lỗi áp dụng màu: {ex.Message}");
        }
        finally
        {
            btnApplyColor.IsEnabled = true;
            pnlProgress.Visibility = Visibility.Collapsed;
        }
    }

    private void BtnSaveImage_Click(object sender, RoutedEventArgs e)
    {
        if (_workingImage == null) return;
        var dlg = new Microsoft.Win32.SaveFileDialog {
            Filter = "PNG Image (*.png)|*.png|JPEG Image (*.jpg)|*.jpg",
            Title = "Lưu ảnh Color Graded",
            FileName = "ColorGraded_" + Path.GetFileName(_currentImagePath)
        };
        if (dlg.ShowDialog() == true)
        {
            _workingImage.Save(dlg.FileName);
            MessageBox.Show("Lưu thành công!", "Đã xong", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    // --- LUT PROCESSING ---
    private LUT3D _currentLut;

    private async void BtnLoadLUT_Click(object sender, RoutedEventArgs e)
    {
        var ofd = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Chọn file LUT (.cube)",
            Filter = "LUT Cube files (*.cube)|*.cube|All files (*.*)|*.*",
            Multiselect = false
        };

        if (ofd.ShowDialog() == true)
        {
            btnLoadLUT.IsEnabled = false;
            try
            {
                _currentLut = await Task.Run(() => LUTParser.ParseCubeFile(ofd.FileName));
                txtCurrentLUT.Text = $"LUT: {_currentLut.Title} ({_currentLut.Size}x{_currentLut.Size}x{_currentLut.Size})";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi load LUT: {ex.Message}");
            }
            btnLoadLUT.IsEnabled = true;
        }
    }

    private async void BtnApplyLUT_Click(object sender, RoutedEventArgs e)
    {
        if (_workingImage == null || _currentLut == null) return;
        
        try
        {
            btnApplyLUT.IsEnabled = false;
            txtStatus.Text = "Đang map màu 3D LUT...";
            pnlProgress.Visibility = Visibility.Visible;

            float intensity = (float)slLutIntensity.Value;

            await Task.Run(() => LUTProcessor.ApplyLUT(_workingImage, _currentLut, intensity));

            UpdatePreviewUI();
            btnSaveImage.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Lỗi map màu LUT: {ex.Message}");
        }
        finally
        {
            btnApplyLUT.IsEnabled = true;
            pnlProgress.Visibility = Visibility.Collapsed;
        }
    }

    // --- COLOR CORRECTION (WHITE BALANCE) ---
    private async void BtnAutoWB_Click(object sender, RoutedEventArgs e)
    {
        if (_workingImage == null) return;
        
        try
        {
            btnAutoWB.IsEnabled = false;
            txtStatus.Text = "Đang chạy cân bằng thuật toán Gray World...";
            pnlProgress.Visibility = Visibility.Visible;

            await Task.Run(() => ColorCorrectionProcessor.ApplyGrayWorld(_workingImage));

            UpdatePreviewUI();
            btnSaveImage.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Lỗi Auto WB: {ex.Message}");
        }
        finally
        {
            btnAutoWB.IsEnabled = true;
            pnlProgress.Visibility = Visibility.Collapsed;
        }
    }

    private async void BtnApplyWBPick_Click(object sender, RoutedEventArgs e)
    {
        if (_workingImage == null) return;
        
        try
        {
            btnApplyWBPick.IsEnabled = false;
            txtStatus.Text = "Đang bù trừ màu gốc...";
            pnlProgress.Visibility = Visibility.Visible;

            var clr = SourceColor; // Dùng màu hút ở Tab Selective
            await Task.Run(() => ColorCorrectionProcessor.ApplyWhitePoint(_workingImage, clr));

            UpdatePreviewUI();
            btnSaveImage.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Lỗi cân bằng tay: {ex.Message}");
        }
        finally
        {
            btnApplyWBPick.IsEnabled = true;
            pnlProgress.Visibility = Visibility.Collapsed;
        }
    }

    // --- COLOR UNIFICATION & NOISE ---
    private async void BtnApplyUnify_Click(object sender, RoutedEventArgs e)
    {
        if (_workingImage == null) return;
        
        try
        {
            if (this.FindName("btnApplyUnify") is Button btn) btn.IsEnabled = false;
            txtStatus.Text = "Đang đồng nhất tone màu...";
            pnlProgress.Visibility = Visibility.Visible;

            var baseClr = SourceColor; // Dùng màu hút ở text source chính
            float intensity = 0.5f;
            if (this.FindName("slUnifyIntensity") is Slider sl)
                intensity = (float)sl.Value / 100f; // 0..100 -> 0..1

            await Task.Run(() => AdvancedColorProcessor.ApplyColorUnification(_workingImage, baseClr, intensity));

            UpdatePreviewUI();
            btnSaveImage.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Lỗi đồng nhất tone: {ex.Message}");
        }
        finally
        {
            if (this.FindName("btnApplyUnify") is Button btn) btn.IsEnabled = true;
            pnlProgress.Visibility = Visibility.Collapsed;
        }
    }

    private async void BtnApplyNoiseReduction_Click(object sender, RoutedEventArgs e)
    {
        if (_workingImage == null) return;
        
        try
        {
            if (this.FindName("btnApplyNoiseReduction") is Button btn) btn.IsEnabled = false;
            txtStatus.Text = "Đang khử nhiễu ảnh...";
            pnlProgress.Visibility = Visibility.Visible;

            await Task.Run(() => AdvancedColorProcessor.ApplyColorNoiseReduction(_workingImage, 1));

            UpdatePreviewUI();
            btnSaveImage.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Lỗi khử nhiễu: {ex.Message}");
        }
        finally
        {
            if (this.FindName("btnApplyNoiseReduction") is Button btn) btn.IsEnabled = true;
            pnlProgress.Visibility = Visibility.Collapsed;
        }
    }
}
