using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Controls;
using ImageTool.Core;

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

        return menu;
    }
}
