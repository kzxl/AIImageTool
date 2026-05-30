using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ImageTool.Core;
using ImageTool.Shared;
using Xunit;

namespace ImageTool.Tests;

public class ImageMetaServiceTests
{
    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "imgtool_meta_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void SetPickMany_SetsAllAndPersists()
    {
        var dir = TempDir();
        try
        {
            var a = Path.Combine(dir, "a.jpg"); File.WriteAllText(a, "x");
            var b = Path.Combine(dir, "b.jpg"); File.WriteAllText(b, "x");
            var c = Path.Combine(dir, "c.jpg"); File.WriteAllText(c, "x");

            var svc = new ImageMetaService();
            svc.SetPickMany(new[] { a, b, c }, PickFlag.Reject);

            Assert.Equal(PickFlag.Reject, svc.Get(a).Pick);
            Assert.Equal(PickFlag.Reject, svc.Get(b).Pick);
            Assert.Equal(PickFlag.Reject, svc.Get(c).Pick);

            // Đọc lại từ đĩa bằng instance mới -> đã ghi sidecar.
            var svc2 = new ImageMetaService();
            Assert.Equal(PickFlag.Reject, svc2.Get(b).Pick);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void SetRatingMany_FiresEventPerImage()
    {
        var dir = TempDir();
        try
        {
            var a = Path.Combine(dir, "a.jpg"); File.WriteAllText(a, "x");
            var b = Path.Combine(dir, "b.jpg"); File.WriteAllText(b, "x");

            var svc = new ImageMetaService();
            int events = 0;
            svc.MetaChanged += (_, _) => events++;
            svc.SetRatingMany(new[] { a, b }, 4);

            Assert.Equal(2, events);
            Assert.Equal(4, svc.Get(a).Rating);
            Assert.Equal(4, svc.Get(b).Rating);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void SetLabelMany_PreservesOtherFields()
    {
        var dir = TempDir();
        try
        {
            var a = Path.Combine(dir, "a.jpg"); File.WriteAllText(a, "x");
            var svc = new ImageMetaService();
            svc.SetRating(a, 3);
            svc.SetLabelMany(new[] { a }, ColorLabel.Green);

            var m = svc.Get(a);
            Assert.Equal(ColorLabel.Green, m.Label);
            Assert.Equal(3, m.Rating); // rating cũ giữ nguyên
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void SetPickMany_GroupsAcrossFolders()
    {
        var dir1 = TempDir();
        var dir2 = TempDir();
        try
        {
            var a = Path.Combine(dir1, "a.jpg"); File.WriteAllText(a, "x");
            var b = Path.Combine(dir2, "b.jpg"); File.WriteAllText(b, "x");

            var svc = new ImageMetaService();
            svc.SetPickMany(new[] { a, b }, PickFlag.Pick);

            // Mỗi folder có sidecar riêng.
            Assert.True(File.Exists(Path.Combine(dir1, ".imgtool.json")));
            Assert.True(File.Exists(Path.Combine(dir2, ".imgtool.json")));
            Assert.Equal(PickFlag.Pick, svc.Get(a).Pick);
            Assert.Equal(PickFlag.Pick, svc.Get(b).Pick);
        }
        finally { Directory.Delete(dir1, true); Directory.Delete(dir2, true); }
    }
}
