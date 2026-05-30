using System;
using System.IO;
using ImageTool.Shared;
using Xunit;

namespace ImageTool.Tests;

public class AppLogTests
{
    [Fact]
    public void Warn_And_Error_WriteLines()
    {
        var path = Path.Combine(Path.GetTempPath(), "imgtool_log_" + Guid.NewGuid().ToString("N") + ".log");
        var prev = AppLog.Path;
        AppLog.Path = path;
        try
        {
            AppLog.Warn("Test", "cảnh báo");
            AppLog.Error("Test", "lỗi", new InvalidOperationException("boom"));
            var text = File.ReadAllText(path);
            Assert.Contains("WARN Test: cảnh báo", text);
            Assert.Contains("ERROR Test: lỗi", text);
            Assert.Contains("InvalidOperationException: boom", text);
        }
        finally
        {
            AppLog.Path = prev;
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void Logger_NeverThrows_OnBadPath()
    {
        var prev = AppLog.Path;
        AppLog.Path = "Z:\\nonexistent_dir_xyz\\sub\\app.log";
        try
        {
            // không được ném dù path không ghi được.
            AppLog.Warn("Test", "x");
            AppLog.Error("Test", "y", null);
        }
        finally { AppLog.Path = prev; }
    }
}
