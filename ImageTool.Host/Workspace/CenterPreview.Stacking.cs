using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using ImageTool.Shared;

namespace ImageTool.Host.Workspace;

// Grid stacking UI (8.7): gom ảnh chụp liên tiếp thành nhóm, chỉ hiện cover + badge số lượng.
public partial class CenterPreview
{
    private bool _stacked;
    private List<ThumbItem>? _allGridBackup; // toàn bộ item trước khi gom (để mở lại)

    private void BtnStack_Click(object sender, RoutedEventArgs e) => ToggleStacking();

    private void ToggleStacking()
    {
        if (_stacked)
        {
            // mở nhóm: khôi phục toàn bộ.
            if (_allGridBackup != null)
            {
                foreach (var it in _allGridBackup) it.StackCount = 0;
                GridItems = new ObservableCollection<ThumbItem>(_allGridBackup);
                icGrid.ItemsSource = GridItems;
                _allGridBackup = null;
            }
            _stacked = false;
            btnStack.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2D, 0x2D, 0x30));
            return;
        }

        var current = GridItems.ToList();
        if (current.Count == 0) return;
        _allGridBackup = current;

        // gom theo thời gian sửa file (xấp xỉ thời gian chụp khi không có EXIF nhanh).
        var byPath = current.ToDictionary(t => t.ImagePath, StringComparer.OrdinalIgnoreCase);
        var timed = current.Select(t =>
        {
            DateTime when;
            try { when = File.GetLastWriteTime(t.ImagePath); } catch { when = DateTime.MinValue; }
            return (t.ImagePath, when);
        });

        var stacks = ImageStacker.StackByTime(timed, thresholdSeconds: 3.0);
        var covers = new List<ThumbItem>();
        foreach (var st in stacks)
        {
            if (st.Items.Count == 0) continue;
            if (!byPath.TryGetValue(st.Cover, out var cover)) continue;
            cover.StackCount = st.Items.Count;
            covers.Add(cover);
        }

        GridItems = new ObservableCollection<ThumbItem>(covers);
        icGrid.ItemsSource = GridItems;
        _stacked = true;
        btnStack.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3D, 0x7E, 0xFF));
        SetMode(LighttableMode.Grid);
    }
}
