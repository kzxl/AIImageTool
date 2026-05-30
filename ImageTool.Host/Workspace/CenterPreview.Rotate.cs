using System.Windows;

namespace ImageTool.Host.Workspace;

// Nút xoay/lật nhanh ở mode bar trung tâm — uỷ quyền cho DevelopPanel (history-aware, non-destructive).
public partial class CenterPreview
{
    private void BtnRotateLeft_Click(object sender, RoutedEventArgs e) => _developPanel?.RotateActive(-1);
    private void BtnRotateRight_Click(object sender, RoutedEventArgs e) => _developPanel?.RotateActive(1);
    private void BtnFlipH_Click(object sender, RoutedEventArgs e) => _developPanel?.FlipActive(true);
}
