-- @slider intensity (0..1, def: 1, "Intensity")
-- @slider contrast_boost (0..50, def: 10, "Contrast Boost")

-- 1. Tăng tương phản nhanh bằng C# helper để giữ hiệu năng
if contrast_boost > 0 then
    AdjustBrightnessContrast(pixels, 0.0, contrast_boost * intensity)
end

-- 2. Nhuộm tông màu Vintage giả lập phim cổ điển (giảm saturation, áp vàng ấm cho highlights)
local len = pixels.Length
for i = 0, len - 1, 4 do
    local r = pixels[i]
    local g = pixels[i + 1]
    local b = pixels[i + 2]

    -- Tính độ sáng (Luminance Rec.709)
    local lum = 0.2126 * r + 0.7152 * g + 0.0722 * b

    -- Giảm nhẹ độ bão hòa dựa trên Intensity
    local satFactor = 1.0 - (0.3 * intensity)
    r = lum + (r - lum) * satFactor
    g = lum + (g - lum) * satFactor
    b = lum + (b - lum) * satFactor

    -- Nhuộm màu vàng ấm (vàng/cam nhẹ) ở vùng sáng (Highlights)
    if lum > 0.5 then
        local factor = (lum - 0.5) * 0.12 * intensity
        r = r + factor
        g = g + (factor * 0.5)
        b = b - factor
    end

    pixels[i] = r
    pixels[i + 1] = g
    pixels[i + 2] = b
end
