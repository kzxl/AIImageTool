using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ImageTool.Core;
using ImageTool.Imaging;
using ImageTool.Shared;

namespace ImageTool.Host;

/// <summary>
/// Dựng ContextMenu dùng chung cho thumbnail/ảnh ở mọi view (Browser, Filmstrip, Grid).
/// Gồm: Copy/Paste Develop settings, Reset, Rating, Color Label, Pick flag, mở trong Explorer.
/// Thao tác áp cho toàn bộ selection nếu ảnh phải-chuột nằm trong selection, ngược lại cho riêng ảnh đó.
/// </summary>
public static class ImageContextMenu
{
    public static ContextMenu Build(
        string imagePath,
        IWorkspaceService workspace,
        IImageMetaService meta,
        IHistoryService history,
        DevelopClipboard clipboard)
    {
        // Tập ảnh đích: nếu ảnh thuộc selection nhiều ảnh -> áp cả selection; ngược lại chỉ ảnh này.
        List<string> Targets()
        {
            var sel = workspace.Selection;
            if (sel.Count > 1 && sel.Contains(imagePath)) return sel.ToList();
            return new List<string> { imagePath };
        }

        var menu = new ContextMenu();

        // --- Develop settings ---
        var miCopy = new MenuItem { Header = "Copy Settings\tCtrl+Shift+C" };
        miCopy.Click += (_, _) => clipboard.Copy(history, imagePath);
        menu.Items.Add(miCopy);

        var miPaste = new MenuItem { Header = "Paste Settings\tCtrl+Shift+V", IsEnabled = clipboard.HasCopied };
        miPaste.Click += (_, _) => clipboard.PasteToMany(history, Targets());
        menu.Items.Add(miPaste);

        // --- Selective paste theo module (D6.1) ---
        var available = clipboard.ModulesAvailable();
        var miPasteSel = new MenuItem { Header = "Paste Settings (chọn module)", IsEnabled = clipboard.HasCopied && available.Count > 0 };
        if (available.Count > 0)
        {
            var picked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var mod in available)
            {
                var modKey = mod.Key;
                var item = new MenuItem { Header = mod.Label, IsCheckable = true, StaysOpenOnClick = true };
                item.Checked += (_, _) => picked.Add(modKey);
                item.Unchecked += (_, _) => picked.Remove(modKey);
                miPasteSel.Items.Add(item);
            }
            miPasteSel.Items.Add(new Separator());
            var apply = new MenuItem { Header = "Áp module đã chọn" };
            apply.Click += (_, _) =>
            {
                if (picked.Count == 0) return;
                foreach (var t in Targets())
                    clipboard.PasteModulesTo(history, t, picked);
            };
            miPasteSel.Items.Add(apply);
        }
        menu.Items.Add(miPasteSel);

        var miReset = new MenuItem { Header = "Reset Develop" };
        miReset.Click += (_, _) =>
        {
            foreach (var t in Targets())
                history.UpsertGroup(t, DevelopClipboard.DevelopPluginId, Array.Empty<EditOperation>());
        };
        menu.Items.Add(miReset);

        menu.Items.Add(new Separator());

        // --- Rating ---
        var miRating = new MenuItem { Header = "Rating" };
        for (int r = 0; r <= 5; r++)
        {
            int rr = r;
            var item = new MenuItem { Header = r == 0 ? "No rating" : new string('★', r) };
            item.Click += (_, _) => { foreach (var t in Targets()) meta.SetRating(t, rr); };
            miRating.Items.Add(item);
        }
        menu.Items.Add(miRating);

        // --- Color Label ---
        var miLabel = new MenuItem { Header = "Color Label" };
        foreach (var (name, lbl) in new[]
        {
            ("None", ColorLabel.None), ("Red", ColorLabel.Red), ("Yellow", ColorLabel.Yellow),
            ("Green", ColorLabel.Green), ("Blue", ColorLabel.Blue), ("Purple", ColorLabel.Purple)
        })
        {
            var ll = lbl;
            var item = new MenuItem { Header = name };
            item.Click += (_, _) => { foreach (var t in Targets()) meta.SetLabel(t, ll); };
            miLabel.Items.Add(item);
        }
        menu.Items.Add(miLabel);

        // --- Pick flag ---
        var miPick = new MenuItem { Header = "Flag" };
        foreach (var (name, pf) in new[]
        {
            ("Pick (P)", PickFlag.Pick), ("Reject (X)", PickFlag.Reject), ("Unflag (U)", PickFlag.None)
        })
        {
            var pp = pf;
            var item = new MenuItem { Header = name };
            item.Click += (_, _) => { foreach (var t in Targets()) meta.SetPick(t, pp); };
            miPick.Items.Add(item);
        }
        menu.Items.Add(miPick);

        menu.Items.Add(new Separator());

        // --- File ops ---
        var miReveal = new MenuItem { Header = "Show in Explorer" };
        miReveal.Click += (_, _) =>
        {
            try
            {
                if (File.Exists(imagePath))
                    Process.Start("explorer.exe", $"/select,\"{imagePath}\"");
            }
            catch { }
        };
        menu.Items.Add(miReveal);

        var miCopyPath = new MenuItem { Header = "Copy File Path" };
        miCopyPath.Click += (_, _) =>
        {
            try { System.Windows.Clipboard.SetText(imagePath); } catch { }
        };
        menu.Items.Add(miCopyPath);

        // --- Batch Rename (13.7) ---
        var miRename = new MenuItem { Header = "Batch Rename..." };
        miRename.Click += (_, _) =>
        {
            var targets = Targets();
            var dlg = new BatchRenameDialog(targets)
            {
                Owner = System.Windows.Application.Current?.MainWindow
            };
            dlg.ShowDialog();
        };
        menu.Items.Add(miRename);

        // --- Merge nhiều ảnh (HDR / Focus Stack) ---
        var miMerge = new MenuItem { Header = "Merge..." };
        var miHdr = new MenuItem { Header = "Merge to HDR (Exposure Fusion)" };
        miHdr.Click += (_, _) => RunMerge(Targets(), MergeService.Mode.Hdr);
        var miFocus = new MenuItem { Header = "Focus Stack (nét toàn bộ)" };
        miFocus.Click += (_, _) => RunMerge(Targets(), MergeService.Mode.FocusStack);
        miMerge.Items.Add(miHdr);
        miMerge.Items.Add(miFocus);
        menu.Items.Add(miMerge);

        return menu;
    }

    /// <summary>Decode + ghép các ảnh selection (HDR/Focus) ở thread nền, báo kết quả qua MessageBox.</summary>
    private static async void RunMerge(List<string> targets, MergeService.Mode mode)
    {
        string label = mode == MergeService.Mode.FocusStack ? "Focus Stack" : "Merge to HDR";
        if (targets.Count < 2)
        {
            MessageBox.Show($"Hãy chọn ít nhất 2 ảnh để {label}.", label, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            string outPath = await System.Threading.Tasks.Task.Run(() =>
            {
                var decoders = ImageDecoderRegistry.CreateDefault();
                return MergeService.Merge(targets, mode, decoders);
            });
            MessageBox.Show($"{label} xong: {Path.GetFileName(outPath)}", label, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            AppLog.Error("ImageContextMenu.Merge", label, ex);
            MessageBox.Show($"{label} lỗi: {ex.Message}", label, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
