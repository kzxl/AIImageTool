using System;
using System.IO;
using System.Threading;
using ImageTool.Shared;
using Xunit;

namespace ImageTool.Tests;

public class FolderWatcherTests
{
    [Theory]
    [InlineData("photo.jpg", true)]
    [InlineData("a.PNG", true)]
    [InlineData("raw.cr3", true)]
    [InlineData("scan.tiff", true)]
    [InlineData("notes.txt", false)]
    [InlineData("movie.mp4", false)]
    [InlineData("noext", false)]
    public void IsImageFile_FiltersByExtension(string name, bool expected)
        => Assert.Equal(expected, FolderWatcher.IsImageFile(name));

    [Fact]
    public void Start_OnMissingFolder_NotWatching()
    {
        using var w = new FolderWatcher();
        w.Start(Path.Combine(Path.GetTempPath(), "nonexistent_" + Guid.NewGuid().ToString("N")));
        Assert.False(w.IsWatching);
    }

    [Fact]
    public void DetectsNewImage_AddedAfterStart()
    {
        var dir = Path.Combine(Path.GetTempPath(), "imgtool_watch_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            using var w = new FolderWatcher();
            string? detected = null;
            var ev = new ManualResetEventSlim(false);
            w.ImageAdded += (_, path) => { detected = path; ev.Set(); };
            w.Start(dir);
            Assert.True(w.IsWatching);

            // Tạo file ảnh giả (ghi 1 lần, đủ để watcher coi là ổn định).
            string newImg = Path.Combine(dir, "new.png");
            File.WriteAllBytes(newImg, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

            // Chờ tối đa 8s (watcher có WaitFileReady ~0.5-1s).
            bool got = ev.Wait(8000);
            Assert.True(got, "không phát hiện ảnh mới");
            Assert.Equal(newImg, detected);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void ExistingFiles_NotReportedAsNew()
    {
        var dir = Path.Combine(Path.GetTempPath(), "imgtool_watch_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            // File có sẵn TRƯỚC khi watch.
            File.WriteAllBytes(Path.Combine(dir, "old.jpg"), new byte[] { 1, 2, 3 });

            using var w = new FolderWatcher();
            int count = 0;
            w.ImageAdded += (_, _) => Interlocked.Increment(ref count);
            w.Start(dir);

            Thread.Sleep(400); // không có file mới -> không event
            Assert.Equal(0, count);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void Stop_DisablesWatching()
    {
        var dir = Path.Combine(Path.GetTempPath(), "imgtool_watch_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var w = new FolderWatcher();
            w.Start(dir);
            Assert.True(w.IsWatching);
            w.Stop();
            Assert.False(w.IsWatching);
            w.Dispose();
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
