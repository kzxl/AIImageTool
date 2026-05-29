using System;
using System.Collections.Generic;
using ImageTool.Shared;
using Xunit;

namespace ImageTool.Tests;

public class ImageStackerTests
{
    private static (string, DateTime) P(string name, int sec)
        => (name, new DateTime(2026, 5, 29, 10, 0, 0).AddSeconds(sec));

    [Fact]
    public void StackByTime_GroupsBurst()
    {
        var items = new List<(string, DateTime)>
        {
            P("a.jpg", 0), P("b.jpg", 1), P("c.jpg", 2),   // burst trong 2s
            P("d.jpg", 30),                                 // tách
            P("e.jpg", 31),                                 // burst với d
        };
        var stacks = ImageStacker.StackByTime(items, 2.0);
        Assert.Equal(2, stacks.Count);
        Assert.Equal(3, stacks[0].Items.Count);
        Assert.Equal(2, stacks[1].Items.Count);
        Assert.True(stacks[0].IsStack);
    }

    [Fact]
    public void StackByTime_SingleImages_NotStacked()
    {
        var items = new List<(string, DateTime)> { P("a.jpg", 0), P("b.jpg", 60), P("c.jpg", 120) };
        var stacks = ImageStacker.StackByTime(items, 2.0);
        Assert.Equal(3, stacks.Count);
        Assert.All(stacks, s => Assert.False(s.IsStack));
    }

    [Fact]
    public void StackByTime_CoverIsFirst()
    {
        var items = new List<(string, DateTime)> { P("z.jpg", 1), P("a.jpg", 0) };
        var stacks = ImageStacker.StackByTime(items, 5.0);
        Assert.Single(stacks);
        // sắp theo thời gian -> a (sec 0) là cover.
        Assert.Equal("a.jpg", stacks[0].Cover);
    }

    [Fact]
    public void StackByTime_Empty()
    {
        var stacks = ImageStacker.StackByTime(new List<(string, DateTime)>());
        Assert.Empty(stacks);
    }

    [Fact]
    public void StackByBaseName_GroupsVariants()
    {
        var paths = new[]
        {
            @"C:\p\IMG_1234.jpg",
            @"C:\p\IMG_1234-Edit.jpg",
            @"C:\p\IMG_9999.jpg",
        };
        var stacks = ImageStacker.StackByBaseName(paths);
        // IMG_1234 + IMG_1234-Edit gộp 1 stack; IMG_9999 riêng.
        Assert.Equal(2, stacks.Count);
        Assert.Equal(2, stacks[0].Items.Count);
        Assert.Single(stacks[1].Items);
    }

    [Fact]
    public void BaseName_StripsSuffix()
    {
        Assert.Equal("IMG_1234", ImageStacker.BaseName("IMG_1234"));  // ID số -> giữ
        Assert.Equal("IMG_1234", ImageStacker.BaseName("IMG_1234-Edit")); // bỏ hậu tố chữ
        Assert.Equal("photo", ImageStacker.BaseName("photo-edit"));
        Assert.Equal("plain", ImageStacker.BaseName("plain"));
    }
}
