using System.Collections.ObjectModel;
using ImageTool.Core;

namespace ImageTool.Shared;

public class WorkspaceService : IWorkspaceService
{
    private static readonly string[] _supportedExtensions = { ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".tif", ".tiff",
        ".cr2", ".cr3", ".nef", ".arw", ".dng", ".raf", ".rw2", ".orf", ".pef", ".srw", ".raw", ".nrw", ".sr2" };

    private readonly ObservableCollection<string> _images = new();
    private readonly ObservableCollection<string> _selection = new();
    private readonly ReadOnlyObservableCollection<string> _imagesRo;
    private readonly ReadOnlyObservableCollection<string> _selectionRo;
    private readonly IImageMetaService? _meta;
    private List<string> _allImages = new();

    public WorkspaceService() : this(null) { }

    public WorkspaceService(IImageMetaService? meta)
    {
        _meta = meta;
        _imagesRo = new ReadOnlyObservableCollection<string>(_images);
        _selectionRo = new ReadOnlyObservableCollection<string>(_selection);
    }

    public string? CurrentFolder { get; private set; }
    public string? CurrentViewName { get; private set; }
    public ReadOnlyObservableCollection<string> Images => _imagesRo;
    public ReadOnlyObservableCollection<string> Selection => _selectionRo;
    public string? ActiveImage { get; private set; }
    public WorkspaceFilter Filter { get; } = new();
    public WorkspaceSort Sort { get; set; } = WorkspaceSort.NameAsc;

    public event EventHandler<FolderOpenedEventArgs>? FolderOpened;
    public event EventHandler<ImageSelectedEventArgs>? ActiveImageChanged;
    public event EventHandler<BatchSelectionChangedEventArgs>? SelectionChanged;
    public event EventHandler? ImagesRefreshed;

    public void OpenFolder(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath)) return;

        CurrentFolder = folderPath;
        CurrentViewName = null;
        _selection.Clear();
        _images.Clear();

        // Bước 1: snapshot file list nhanh — chạy nền để không block UI nếu folder lớn
        Task.Run(() =>
        {
            List<string> all;
            try
            {
                all = Directory
                    .EnumerateFiles(folderPath, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(p => _supportedExtensions.Contains(Path.GetExtension(p).ToLowerInvariant()))
                    .ToList();
            }
            catch { all = new List<string>(); }

            _allImages = all;
            var visible = ApplyFilterAndSortInternal();

            // Bước 2: marshal về UI thread bằng SynchronizationContext nếu có,
            // nếu không thì raise event trực tiếp (caller chịu trách nhiệm marshal)
            void Apply()
            {
                _images.Clear();
                foreach (var p in visible) _images.Add(p);

                var prev = ActiveImage;
                ActiveImage = null;
                FolderOpened?.Invoke(this, new FolderOpenedEventArgs(folderPath, visible));
                if (prev != null) ActiveImageChanged?.Invoke(this, new ImageSelectedEventArgs(prev, null));
                SelectionChanged?.Invoke(this, new BatchSelectionChangedEventArgs(Array.Empty<string>()));
            }

            var ctx = _uiContext;
            if (ctx != null) ctx.Post(_ => Apply(), null);
            else Apply();
        });
    }

    public void OpenCatalogView(IEnumerable<string> imagePaths, string? viewName = null)
    {
        CurrentFolder = null;
        CurrentViewName = viewName ?? "All Photos";
        _selection.Clear();

        _allImages = imagePaths.Where(p => File.Exists(p)).ToList();
        var visible = ApplyFilterAndSortInternal();

        void Apply()
        {
            _images.Clear();
            foreach (var p in visible) _images.Add(p);

            var prev = ActiveImage;
            ActiveImage = null;
            FolderOpened?.Invoke(this, new FolderOpenedEventArgs(CurrentViewName, visible));
            if (prev != null) ActiveImageChanged?.Invoke(this, new ImageSelectedEventArgs(prev, null));
            SelectionChanged?.Invoke(this, new BatchSelectionChangedEventArgs(Array.Empty<string>()));
        }

        var ctx = _uiContext;
        if (ctx != null) ctx.Post(_ => Apply(), null);
        else Apply();
    }

    private readonly System.Threading.SynchronizationContext? _uiContext = System.Threading.SynchronizationContext.Current;

    public void ApplyFilterAndSort()
    {
        var visible = ApplyFilterAndSortInternal();
        _images.Clear();
        foreach (var p in visible) _images.Add(p);
        ImagesRefreshed?.Invoke(this, EventArgs.Empty);
        FolderOpened?.Invoke(this, new FolderOpenedEventArgs(CurrentFolder ?? "", visible));
    }

    private List<string> ApplyFilterAndSortInternal()
    {
        // Project sang tuple một lần để mọi key sort/filter chỉ tính 1 lần per item.
        // Tránh OrderBy gọi _meta.Get(p) hoặc FileInfo nhiều lần khi enumerate.
        var rows = new List<Row>(_allImages.Count);
        bool needTime = Sort is WorkspaceSort.DateAsc or WorkspaceSort.DateDesc;
        bool needSize = Sort is WorkspaceSort.SizeAsc or WorkspaceSort.SizeDesc;
        bool needMeta = _meta != null && (
            Filter.MinRating > 0 || Filter.RequiredLabel.HasValue || Filter.RequiredPick.HasValue
            || Filter.HideRejected || Sort == WorkspaceSort.RatingDesc);

        string? search = string.IsNullOrWhiteSpace(Filter.Search) ? null : Filter.Search.Trim();
        // Search cũng cần meta (để khớp keyword/tags), không chỉ tên file.
        bool needMetaForSearch = search != null && _meta != null;

        foreach (var p in _allImages)
        {
            var name = Path.GetFileName(p);

            ImageMeta? m = (needMeta || needMetaForSearch) ? _meta!.Get(p) : null;

            // Search: khớp tên file HOẶC keyword/tags (phân cấp, không phân biệt hoa thường).
            if (search != null)
            {
                bool nameMatch = name.Contains(search, StringComparison.OrdinalIgnoreCase);
                bool tagMatch = m != null && m.Tags.Count > 0 && KeywordHelper.Matches(m.Tags, search);
                if (!nameMatch && !tagMatch) continue;
            }

            if (m != null)
            {
                if (Filter.MinRating > 0 && m.Rating < Filter.MinRating) continue;
                if (Filter.RequiredLabel.HasValue && m.Label != Filter.RequiredLabel.Value) continue;
                if (Filter.RequiredPick.HasValue && m.Pick != Filter.RequiredPick.Value) continue;
                if (Filter.HideRejected && m.Pick == PickFlag.Reject) continue;
            }

            rows.Add(new Row(
                p,
                name,
                needTime ? SafeWriteTime(p) : DateTime.MinValue,
                needSize ? SafeSize(p) : 0,
                m?.Rating ?? 0));
        }

        rows.Sort(Sort switch
        {
            WorkspaceSort.NameAsc => (a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name),
            WorkspaceSort.NameDesc => (a, b) => StringComparer.OrdinalIgnoreCase.Compare(b.Name, a.Name),
            WorkspaceSort.DateAsc => (a, b) => a.WriteTime.CompareTo(b.WriteTime),
            WorkspaceSort.DateDesc => (a, b) => b.WriteTime.CompareTo(a.WriteTime),
            WorkspaceSort.SizeAsc => (a, b) => a.Size.CompareTo(b.Size),
            WorkspaceSort.SizeDesc => (a, b) => b.Size.CompareTo(a.Size),
            WorkspaceSort.RatingDesc => (a, b) =>
            {
                int c = b.Rating.CompareTo(a.Rating);
                return c != 0 ? c : StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name);
            },
            _ => (a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name)
        });

        var result = new List<string>(rows.Count);
        foreach (var r in rows) result.Add(r.Path);
        return result;
    }

    private readonly record struct Row(string Path, string Name, DateTime WriteTime, long Size, int Rating);

    private static DateTime SafeWriteTime(string p)
    {
        try { return File.GetLastWriteTimeUtc(p); } catch { return DateTime.MinValue; }
    }
    private static long SafeSize(string p)
    {
        try { return new FileInfo(p).Length; } catch { return 0; }
    }

    public void SetActiveImage(string? path)
    {
        if (ActiveImage == path) return;
        var prev = ActiveImage;
        ActiveImage = path;
        ActiveImageChanged?.Invoke(this, new ImageSelectedEventArgs(prev, path));
    }

    public void SetSelection(IEnumerable<string> paths)
    {
        _selection.Clear();
        foreach (var p in paths.Distinct()) _selection.Add(p);
        SelectionChanged?.Invoke(this, new BatchSelectionChangedEventArgs(_selection.ToList()));
    }

    public void AddToSelection(string path)
    {
        if (_selection.Contains(path)) return;
        _selection.Add(path);
        SelectionChanged?.Invoke(this, new BatchSelectionChangedEventArgs(_selection.ToList()));
    }

    public void RemoveFromSelection(string path)
    {
        if (!_selection.Remove(path)) return;
        SelectionChanged?.Invoke(this, new BatchSelectionChangedEventArgs(_selection.ToList()));
    }

    public void ClearSelection()
    {
        if (_selection.Count == 0) return;
        _selection.Clear();
        SelectionChanged?.Invoke(this, new BatchSelectionChangedEventArgs(Array.Empty<string>()));
    }
}
