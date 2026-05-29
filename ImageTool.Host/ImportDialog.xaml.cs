using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using ImageTool.Core;

namespace ImageTool.Host;

public class ImportFileItem
{
    public string FilePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public bool IsChecked { get; set; } = true;
    public BitmapImage? Thumbnail { get; set; }
}

public partial class ImportDialog : Window
{
    private static readonly string[] SupportedExtensions = { ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".tif", ".tiff" };

    private readonly ICatalogService _catalog;
    private readonly IThumbnailService _thumbnails;
    private readonly IWorkspaceService _workspace;
    private readonly ObservableCollection<ImportFileItem> _files = new();

    public ImportDialog(ICatalogService catalog, IThumbnailService thumbnails, IWorkspaceService workspace)
        : this(catalog, thumbnails, workspace, null) { }

    public ImportDialog(ICatalogService catalog, IThumbnailService thumbnails, IWorkspaceService workspace, string? initialSourceFolder)
    {
        _catalog = catalog;
        _thumbnails = thumbnails;
        _workspace = workspace;
        InitializeComponent();

        lstPreview.ItemsSource = _files;

        var defaultLib = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "ImageTool Library");
        txtLibraryPath.Text = defaultLib;

        if (!string.IsNullOrWhiteSpace(initialSourceFolder) && Directory.Exists(initialSourceFolder))
        {
            txtSourcePath.Text = initialSourceFolder;
            Loaded += (s, e) => LoadSourceFiles(initialSourceFolder);
        }
    }

    private void BtnBrowseSource_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Select source folder to import from" };
        if (dlg.ShowDialog() == true)
        {
            txtSourcePath.Text = dlg.FolderName;
            LoadSourceFiles(dlg.FolderName);
        }
    }

    private void LoadSourceFiles(string folderPath)
    {
        _files.Clear();
        var searchOption = chkIncludeSubfolders.IsChecked == true
            ? SearchOption.AllDirectories
            : SearchOption.TopDirectoryOnly;

        try
        {
            var files = Directory.EnumerateFiles(folderPath, "*.*", searchOption)
                .Where(f => SupportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .Take(5000)
                .ToList();

            foreach (var f in files)
            {
                var item = new ImportFileItem
                {
                    FilePath = f,
                    FileName = Path.GetFileName(f),
                    IsChecked = !_catalog.IsImported(f)
                };
                _files.Add(item);
            }

            LoadThumbnailsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error scanning folder: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        UpdateSummary();
    }

    private async void LoadThumbnailsAsync()
    {
        foreach (var item in _files.ToList())
        {
            await Task.Run(() =>
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(item.FilePath);
                    bmp.DecodePixelWidth = 80;
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    bmp.Freeze();
                    item.Thumbnail = bmp;
                }
                catch { }
            });

            Dispatcher.Invoke(() =>
            {
                var idx = _files.IndexOf(item);
                if (idx >= 0)
                {
                    _files.RemoveAt(idx);
                    _files.Insert(idx, item);
                }
            });
        }
    }

    private void LoadDroppedFiles(IEnumerable<string> paths)
    {
        _files.Clear();
        var allFiles = new List<string>();

        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                allFiles.AddRange(Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories)
                    .Where(f => SupportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant())));
            }
            else if (File.Exists(path) && SupportedExtensions.Contains(Path.GetExtension(path).ToLowerInvariant()))
            {
                allFiles.Add(path);
            }
        }

        foreach (var f in allFiles.Take(5000))
        {
            _files.Add(new ImportFileItem
            {
                FilePath = f,
                FileName = Path.GetFileName(f),
                IsChecked = !_catalog.IsImported(f)
            });
        }

        txtSourcePath.Text = $"({allFiles.Count} files dropped)";
        LoadThumbnailsAsync();
        UpdateSummary();
    }

    private void DropZone_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files != null && files.Length > 0)
                LoadDroppedFiles(files);
        }
    }

    private void DropZone_DragEnter(object sender, DragEventArgs e)
    {
        txtDropZone.Text = "Drop to add files";
    }

    private void DropZone_DragLeave(object sender, DragEventArgs e)
    {
        txtDropZone.Text = "or drag files here";
    }

    private void BtnCheckAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var f in _files) f.IsChecked = true;
        RefreshList();
        UpdateSummary();
    }

    private void BtnUncheckAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var f in _files) f.IsChecked = false;
        RefreshList();
        UpdateSummary();
    }

    private void RefreshList()
    {
        var items = _files.ToList();
        _files.Clear();
        foreach (var i in items) _files.Add(i);
    }

    private void RbCopyToLibrary_Checked(object sender, RoutedEventArgs e)
    {
        if (pnlLibraryOptions != null) pnlLibraryOptions.Visibility = Visibility.Visible;
    }

    private void RbCopyToLibrary_Unchecked(object sender, RoutedEventArgs e)
    {
        if (pnlLibraryOptions != null) pnlLibraryOptions.Visibility = Visibility.Collapsed;
    }

    private void BtnBrowseLibrary_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Select library destination folder" };
        if (dlg.ShowDialog() == true)
            txtLibraryPath.Text = dlg.FolderName;
    }

    private void UpdateSummary()
    {
        int checkedCount = _files.Count(f => f.IsChecked);
        long totalSize = 0;
        foreach (var f in _files.Where(f => f.IsChecked))
        {
            try { totalSize += new FileInfo(f.FilePath).Length; } catch { }
        }

        txtFileCount.Text = $"{_files.Count} files";
        txtSummary.Text = $"{checkedCount} files selected\n{totalSize / (1024.0 * 1024.0):F1} MB total";
        btnImport.IsEnabled = checkedCount > 0;
    }

    private async void BtnImport_Click(object sender, RoutedEventArgs e)
    {
        var selected = _files.Where(f => f.IsChecked).Select(f => f.FilePath).ToList();
        if (selected.Count == 0) return;

        var options = new ImportOptions
        {
            Mode = rbCopyToLibrary.IsChecked == true ? ImportMode.CopyToLibrary : ImportMode.AddInPlace,
            DestinationFolder = rbCopyToLibrary.IsChecked == true ? txtLibraryPath.Text : null,
            SubfolderByDate = chkOrganizeByDate.IsChecked == true
        };

        btnImport.IsEnabled = false;
        btnCancel.IsEnabled = false;
        btnBrowseSource.IsEnabled = false;
        pbImport.Visibility = Visibility.Visible;

        var progress = new Progress<ImportProgress>(p =>
        {
            pbImport.Value = (double)p.Completed / p.Total * 100;
            txtSummary.Text = $"Importing {p.Completed}/{p.Total}...\n{p.CurrentFile}";
        });

        try
        {
            int count = await _catalog.ImportAsync(selected, options, progress);
            MessageBox.Show($"Successfully imported {count} photos.", "Import Complete",
                MessageBoxButton.OK, MessageBoxImage.Information);

            var allImages = _catalog.GetAllImages();
            _workspace.OpenCatalogView(allImages.Select(i => i.FilePath).ToList(), "All Photos");
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Import error: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            btnImport.IsEnabled = true;
            btnCancel.IsEnabled = true;
            btnBrowseSource.IsEnabled = true;
            pbImport.Visibility = Visibility.Hidden;
        }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
