using System;
using System.Collections.Generic;
using ImageTool.Shared;
using Xunit;

namespace ImageTool.Tests;

public class FileNameTokenizerTests
{
    private static FileNameTokenizer.Context Ctx(string name = "IMG_001", string ext = "jpg", int idx = 1)
        => new()
        {
            OriginalName = name, Extension = ext, Index = idx,
            Width = 1920, Height = 1080, ParentFolder = "Trip",
            Now = new DateTime(2026, 5, 29, 14, 30, 45)
        };

    [Fact]
    public void Resolve_NameAndExt()
    {
        Assert.Equal("IMG_001.jpg", FileNameTokenizer.Resolve("{name}.{ext}", Ctx()));
    }

    [Fact]
    public void Resolve_SequencePadded()
    {
        Assert.Equal("photo_005", FileNameTokenizer.Resolve("photo_{n:000}", Ctx(idx: 5)));
    }

    [Fact]
    public void Resolve_SequenceUnpadded()
    {
        Assert.Equal("photo_7", FileNameTokenizer.Resolve("photo_{n}", Ctx(idx: 7)));
    }

    [Fact]
    public void Resolve_DateDefaultAndCustom()
    {
        Assert.Equal("2026-05-29", FileNameTokenizer.Resolve("{date}", Ctx()));
        Assert.Equal("20260529", FileNameTokenizer.Resolve("{date:yyyyMMdd}", Ctx()));
    }

    [Fact]
    public void Resolve_Dimensions()
    {
        Assert.Equal("1920x1080", FileNameTokenizer.Resolve("{w}x{h}", Ctx()));
    }

    [Fact]
    public void Resolve_Parent()
    {
        Assert.Equal("Trip_IMG_001", FileNameTokenizer.Resolve("{parent}_{name}", Ctx()));
    }

    [Fact]
    public void Resolve_UnknownTokenKept()
    {
        Assert.Equal("{bogus}", FileNameTokenizer.Resolve("{bogus}", Ctx()));
    }

    [Fact]
    public void Sanitize_RemovesInvalidChars()
    {
        var s = FileNameTokenizer.Resolve("a/b:c{name}", Ctx(name: "x"));
        Assert.DoesNotContain("/", s);
        Assert.DoesNotContain(":", s);
    }

    [Fact]
    public void ResolveBatch_SequentialIndices()
    {
        var paths = new List<string> { @"C:\a\one.jpg", @"C:\a\two.png", @"C:\a\three.jpg" };
        var result = FileNameTokenizer.ResolveBatch(paths, "shot_{n:00}", startIndex: 1, now: DateTime.Now);
        Assert.Equal("shot_01.jpg", result[0].NewName);
        Assert.Equal("shot_02.png", result[1].NewName);
        Assert.Equal("shot_03.jpg", result[2].NewName);
    }

    [Fact]
    public void ResolveBatch_DeduplicatesCollisions()
    {
        // pattern cố định -> mọi file cùng tên -> phải tự thêm hậu tố.
        var paths = new List<string> { @"C:\a\one.jpg", @"C:\a\two.jpg" };
        var result = FileNameTokenizer.ResolveBatch(paths, "same", now: DateTime.Now);
        Assert.Equal("same.jpg", result[0].NewName);
        Assert.Equal("same_1.jpg", result[1].NewName);
    }

    [Fact]
    public void ResolveBatch_PreservesExtension()
    {
        var paths = new List<string> { @"C:\a\photo.JPEG" };
        var result = FileNameTokenizer.ResolveBatch(paths, "{name}_edit", now: DateTime.Now);
        Assert.Equal("photo_edit.JPEG", result[0].NewName);
    }
}
