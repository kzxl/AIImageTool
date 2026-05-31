using System.Collections.Generic;
using ImageTool.Shared;
using Xunit;

namespace ImageTool.Tests;

public class WebGalleryTests
{
    private static List<WebGallery.Entry> Sample() => new()
    {
        new WebGallery.Entry { Thumb = "thumbs/a.jpg", Large = "large/a.jpg", Caption = "Photo A" },
        new WebGallery.Entry { Thumb = "thumbs/b.jpg", Large = "large/b.jpg", Caption = "Photo B" },
    };

    [Fact]
    public void BuildHtml_ContainsDoctypeAndTitle()
    {
        var html = WebGallery.BuildHtml(Sample(), new WebGallery.Options { Title = "My Trip" });
        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Contains("<title>My Trip</title>", html);
    }

    [Fact]
    public void BuildHtml_IncludesAllImages()
    {
        var html = WebGallery.BuildHtml(Sample(), new WebGallery.Options());
        Assert.Contains("thumbs/a.jpg", html);
        Assert.Contains("large/a.jpg", html);
        Assert.Contains("thumbs/b.jpg", html);
        Assert.Contains("data-large=\"large/b.jpg\"", html);
    }

    [Fact]
    public void BuildHtml_EscapesHtmlInCaptionAndTitle()
    {
        var entries = new List<WebGallery.Entry>
        {
            new WebGallery.Entry { Thumb = "t.jpg", Large = "l.jpg", Caption = "a<b>&\"c\"" },
        };
        var html = WebGallery.BuildHtml(entries, new WebGallery.Options { Title = "<script>x</script>" });
        Assert.DoesNotContain("<script>x</script>", html);   // title bị escape
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("a&lt;b&gt;&amp;&quot;c&quot;", html);
    }

    [Fact]
    public void BuildHtml_RespectsColumnCount()
    {
        var html = WebGallery.BuildHtml(Sample(), new WebGallery.Options { Columns = 5 });
        Assert.Contains("repeat(5,1fr)", html);
    }

    [Fact]
    public void BuildHtml_ColumnsClampedToValidRange()
    {
        var html = WebGallery.BuildHtml(Sample(), new WebGallery.Options { Columns = 99 });
        Assert.Contains("repeat(8,1fr)", html); // clamp max 8
    }

    [Fact]
    public void BuildHtml_HasLightboxScript()
    {
        var html = WebGallery.BuildHtml(Sample(), new WebGallery.Options());
        Assert.Contains("addEventListener('click'", html);
        Assert.Contains("Escape", html); // đóng bằng Esc
    }

    [Fact]
    public void BuildHtml_EmptyList_StillValid()
    {
        var html = WebGallery.BuildHtml(new List<WebGallery.Entry>(), new WebGallery.Options());
        Assert.Contains("<div class=\"grid\">", html);
        Assert.EndsWith("</html>\n", html);
    }
}
