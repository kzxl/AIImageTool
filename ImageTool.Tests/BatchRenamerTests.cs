using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ImageTool.Shared;
using Xunit;

namespace ImageTool.Tests;

public class BatchRenamerTests
{
    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "imgtool_rn_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string MakeFile(string dir, string name, string content = "x")
    {
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Execute_RenamesFiles()
    {
        var dir = NewTempDir();
        try
        {
            var a = MakeFile(dir, "one.jpg");
            var b = MakeFile(dir, "two.jpg");
            var plan = new List<(string, string)> { (a, "shot_01.jpg"), (b, "shot_02.jpg") };
            var results = BatchRenamer.Execute(plan);

            Assert.All(results, r => Assert.True(r.Success));
            Assert.True(File.Exists(Path.Combine(dir, "shot_01.jpg")));
            Assert.True(File.Exists(Path.Combine(dir, "shot_02.jpg")));
            Assert.False(File.Exists(a));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Execute_HandlesSwap()
    {
        // hoán đổi a.jpg <-> b.jpg: cần pha tạm để không mất file.
        var dir = NewTempDir();
        try
        {
            var a = MakeFile(dir, "a.jpg", "AAA");
            var b = MakeFile(dir, "b.jpg", "BBB");
            var plan = new List<(string, string)> { (a, "b.jpg"), (b, "a.jpg") };
            var results = BatchRenamer.Execute(plan);

            Assert.All(results, r => Assert.True(r.Success));
            // nội dung đã hoán đổi.
            Assert.Equal("AAA", File.ReadAllText(Path.Combine(dir, "b.jpg")));
            Assert.Equal("BBB", File.ReadAllText(Path.Combine(dir, "a.jpg")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Execute_NoChange_Succeeds()
    {
        var dir = NewTempDir();
        try
        {
            var a = MakeFile(dir, "keep.jpg");
            var results = BatchRenamer.Execute(new List<(string, string)> { (a, "keep.jpg") });
            Assert.True(results[0].Success);
            Assert.True(File.Exists(a));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Execute_MissingFile_Fails()
    {
        var dir = NewTempDir();
        try
        {
            var ghost = Path.Combine(dir, "ghost.jpg");
            var results = BatchRenamer.Execute(new List<(string, string)> { (ghost, "new.jpg") });
            Assert.False(results[0].Success);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Execute_WithTokenizer_EndToEnd()
    {
        var dir = NewTempDir();
        try
        {
            var f1 = MakeFile(dir, "DSC1.jpg");
            var f2 = MakeFile(dir, "DSC2.jpg");
            var plan = FileNameTokenizer.ResolveBatch(new[] { f1, f2 }, "trip_{n:000}", startIndex: 1, now: DateTime.Now);
            var results = BatchRenamer.Execute(plan);
            Assert.All(results, r => Assert.True(r.Success));
            Assert.True(File.Exists(Path.Combine(dir, "trip_001.jpg")));
            Assert.True(File.Exists(Path.Combine(dir, "trip_002.jpg")));
        }
        finally { Directory.Delete(dir, true); }
    }
}
