using System;
using System.Collections.Generic;
using ImageTool.Imaging;
using Xunit;

namespace ImageTool.Tests;

public class LuaScriptOpTests
{
    private static LinearImage SolidColor(float r, float g, float b, int w = 4, int h = 4)
    {
        var img = new LinearImage(w, h);
        for (int i = 0; i < img.Pixels.Length; i += 4)
        {
            img.Pixels[i] = r;
            img.Pixels[i + 1] = g;
            img.Pixels[i + 2] = b;
            img.Pixels[i + 3] = 1.0f; // Alpha
        }
        return img;
    }

    [Fact]
    public void LuaScriptOp_NativeLoop_AdjustsPixels()
    {
        var img = SolidColor(0.5f, 0.5f, 0.5f);
        
        // Nạp script "double_red" từ file double_red.lua
        var op = new LuaScriptOp("", new Dictionary<string, string>(), "double_red");
        op.Apply(img, 1.0f);

        Assert.Equal(1.0f, img.Pixels[0]); // R tăng từ 0.5 -> 1.0
        Assert.Equal(0.5f, img.Pixels[1]); // G giữ nguyên 0.5
        Assert.Equal(0.5f, img.Pixels[2]); // B giữ nguyên 0.5
    }

    [Fact]
    public void LuaScriptOp_Helper_AdjustsBrightnessContrast()
    {
        var img = SolidColor(0.2f, 0.2f, 0.2f); // R=G=B=0.2

        // Nạp script "adjust_brightness_contrast" từ file adjust_brightness_contrast.lua
        // Tăng brightness lên 20 (trong dải -255..255)
        var @params = new Dictionary<string, string>
        {
            ["brightness"] = "20",
            ["contrast"] = "0"
        };
        var op = new LuaScriptOp("", @params, "adjust_brightness_contrast");
        op.Apply(img, 1.0f);

        // Chắc chắn các giá trị pixel được điều chỉnh tăng lên
        Assert.True(img.Pixels[0] > 0.2f);
        Assert.True(img.Pixels[1] > 0.2f);
        Assert.True(img.Pixels[2] > 0.2f);
    }

    [Fact]
    public void LuaScriptOp_Helper_Rotate90CW()
    {
        // Tạo ảnh 2x3 (width=2, height=3) để kiểm tra việc xoay đổi kích thước
        var img = new LinearImage(2, 3);
        float[] px = img.Pixels;
        for (int i = 0; i < 6; i++)
        {
            px[i * 4] = i + 1; // R lưu index + 1
            px[i * 4 + 3] = 1.0f; // Alpha
        }

        // Nạp script "rotate_90_cw" từ file rotate_90_cw.lua
        var op = new LuaScriptOp("", new Dictionary<string, string>(), "rotate_90_cw");
        var rotated = op.ApplyResize(img, 1.0f);

        // Ảnh mới phải có width=3, height=2
        Assert.Equal(3, rotated.Width);
        Assert.Equal(2, rotated.Height);

        // Kiểm tra góc xoay 90 độ CW:
        // Cũ 2x3:
        // 1 2
        // 3 4
        // 5 6
        // Mới 3x2:
        // 5 3 1
        // 6 4 2
        float[] rPx = rotated.Pixels;
        Assert.Equal(5.0f, rPx[0]);
        Assert.Equal(3.0f, rPx[4]);
        Assert.Equal(1.0f, rPx[8]);
        Assert.Equal(6.0f, rPx[12]);
        Assert.Equal(4.0f, rPx[16]);
        Assert.Equal(2.0f, rPx[20]);
    }

    [Fact]
    public void LuaScriptOp_Helper_FlipHorizontal()
    {
        // Ảnh 2x2:
        // 1 2
        // 3 4
        var img = new LinearImage(2, 2);
        float[] px = img.Pixels;
        for (int i = 0; i < 4; i++)
        {
            px[i * 4] = i + 1;
            px[i * 4 + 3] = 1.0f;
        }

        // Nạp script "flip_horizontal" từ file flip_horizontal.lua
        var op = new LuaScriptOp("", new Dictionary<string, string>(), "flip_horizontal");
        var flipped = op.ApplyResize(img, 1.0f);

        // Ảnh mới phải có 2x2:
        // 2 1
        // 4 3
        float[] fPx = flipped.Pixels;
        Assert.Equal(2.0f, fPx[0]);
        Assert.Equal(1.0f, fPx[4]);
        Assert.Equal(4.0f, fPx[8]);
        Assert.Equal(3.0f, fPx[12]);
    }
}
