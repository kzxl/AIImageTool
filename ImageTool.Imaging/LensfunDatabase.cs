using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace ImageTool.Imaging;

/// <summary>
/// Đọc database lensfun (5.3): các file XML mô tả profile méo hình (distortion) + tối góc (vignetting)
/// cho từng ống kính. Cho phép TỰ ĐỘNG hiệu chỉnh khi biết tên lens (EXIF) + tiêu cự.
///
/// Schema lensfun (rút gọn):
///   &lt;lensdatabase&gt;&lt;lens&gt;
///     &lt;maker&gt;Canon&lt;/maker&gt; &lt;model&gt;Canon EF 50mm f/1.8&lt;/model&gt;
///     &lt;mount&gt;Canon EF&lt;/mount&gt; &lt;cropfactor&gt;1.0&lt;/cropfactor&gt;
///     &lt;calibration&gt;
///       &lt;distortion model="poly3" focal="50" k1="0.012"/&gt;
///       &lt;vignetting model="pa" focal="50" aperture="1.8" distance="1000" k1="-0.5" k2="0.3" k3="-0.1"/&gt;
///     &lt;/calibration&gt;
///   &lt;/lens&gt;&lt;/lensdatabase&gt;
///
/// Thuần parse XML + so khớp/nội suy -> unit test trực tiếp với XML tổng hợp (không cần DB thật).
/// Việc áp model lên pixel ở <see cref="LensProfileOp"/> (cần verify trên ảnh thật).
/// </summary>
public sealed class LensfunDatabase
{
    /// <summary>1 mốc hiệu chỉnh distortion ở 1 tiêu cự. Model: poly3 (k1), poly5 (k1,k2), ptlens (a,b,c).</summary>
    public sealed class DistortionCalib
    {
        public string Model = "poly3";
        public float Focal;
        public float K1, K2, K3; // poly3: K1; poly5: K1,K2; ptlens: a=K1,b=K2,c=K3
    }

    /// <summary>1 mốc hiệu chỉnh vignetting (model "pa": gain = 1/(1 + k1·r² + k2·r⁴ + k3·r⁶)).</summary>
    public sealed class VignettingCalib
    {
        public float Focal, Aperture, Distance;
        public float K1, K2, K3;
    }

    public sealed class Lens
    {
        public string Maker = "";
        public string Model = "";
        public string Mount = "";
        public float CropFactor = 1f;
        public List<DistortionCalib> Distortions { get; } = new();
        public List<VignettingCalib> Vignettings { get; } = new();
    }

    private readonly List<Lens> _lenses = new();
    public IReadOnlyList<Lens> Lenses => _lenses;

    /// <summary>Parse 1..n nội dung XML lensfun thành database. Bỏ qua file/entry lỗi.</summary>
    public static LensfunDatabase ParseXml(params string[] xmlContents)
    {
        var db = new LensfunDatabase();
        foreach (var xml in xmlContents)
        {
            if (string.IsNullOrWhiteSpace(xml)) continue;
            try { db.AddXml(xml); } catch { /* file lỗi -> bỏ */ }
        }
        return db;
    }

    /// <summary>Nạp mọi file *.xml trong thư mục lensfun (đệ quy). Trả DB rỗng nếu thư mục không có.</summary>
    public static LensfunDatabase LoadDirectory(string dir)
    {
        var db = new LensfunDatabase();
        try
        {
            if (!Directory.Exists(dir)) return db;
            foreach (var f in Directory.EnumerateFiles(dir, "*.xml", SearchOption.AllDirectories))
            {
                try { db.AddXml(File.ReadAllText(f)); } catch { }
            }
        }
        catch { }
        return db;
    }

    private void AddXml(string xml)
    {
        var doc = XDocument.Parse(xml);
        foreach (var le in doc.Descendants("lens"))
        {
            var lens = new Lens
            {
                Maker = (string?)le.Element("maker") ?? "",
                Model = (string?)le.Element("model") ?? "",
                Mount = (string?)le.Element("mount") ?? "",
                CropFactor = F(le.Element("cropfactor")?.Value, 1f),
            };
            if (string.IsNullOrWhiteSpace(lens.Model)) continue;

            foreach (var cal in le.Elements("calibration"))
            {
                foreach (var d in cal.Elements("distortion"))
                {
                    lens.Distortions.Add(new DistortionCalib
                    {
                        Model = (string?)d.Attribute("model") ?? "poly3",
                        Focal = FAttr(d, "focal"),
                        // poly3 dùng k1; ptlens dùng a/b/c; poly5 dùng k1/k2. Đọc cả 2 cách đặt tên.
                        K1 = FAttr(d, "k1", FAttr(d, "a")),
                        K2 = FAttr(d, "k2", FAttr(d, "b")),
                        K3 = FAttr(d, "k3", FAttr(d, "c")),
                    });
                }
                foreach (var v in cal.Elements("vignetting"))
                {
                    lens.Vignettings.Add(new VignettingCalib
                    {
                        Focal = FAttr(v, "focal"),
                        Aperture = FAttr(v, "aperture"),
                        Distance = FAttr(v, "distance"),
                        K1 = FAttr(v, "k1"), K2 = FAttr(v, "k2"), K3 = FAttr(v, "k3"),
                    });
                }
            }
            _lenses.Add(lens);
        }
    }

    /// <summary>
    /// Tìm lens khớp tên (EXIF LensModel). Khớp chính xác trước, rồi khớp chứa nhau (không phân biệt
    /// hoa thường, bỏ qua khoảng trắng thừa). Trả null nếu không có ứng viên.
    /// </summary>
    public Lens? FindLens(string? lensModel)
    {
        if (string.IsNullOrWhiteSpace(lensModel)) return null;
        string q = Normalize(lensModel);

        // 1) khớp chính xác.
        var exact = _lenses.FirstOrDefault(l => Normalize(l.Model) == q);
        if (exact != null) return exact;

        // 2) profile-model là chuỗi con của query hoặc ngược lại; chọn model dài nhất khớp (cụ thể nhất).
        return _lenses
            .Where(l => { var m = Normalize(l.Model); return m.Length > 0 && (q.Contains(m) || m.Contains(q)); })
            .OrderByDescending(l => Normalize(l.Model).Length)
            .FirstOrDefault();
    }

    /// <summary>
    /// Nội suy hệ số distortion cho 1 tiêu cự (mm). Chọn 2 mốc focal gần nhất, nội suy tuyến tính theo
    /// log(focal) (đúng hơn với ống zoom). Trả null nếu lens không có dữ liệu distortion.
    /// </summary>
    public static DistortionCalib? InterpolateDistortion(Lens lens, float focal)
    {
        var list = lens.Distortions;
        if (list.Count == 0) return null;
        if (list.Count == 1) return list[0];

        var sorted = list.OrderBy(d => d.Focal).ToList();
        if (focal <= sorted[0].Focal) return sorted[0];
        if (focal >= sorted[^1].Focal) return sorted[^1];

        for (int i = 0; i < sorted.Count - 1; i++)
        {
            var a = sorted[i]; var b = sorted[i + 1];
            if (focal >= a.Focal && focal <= b.Focal)
            {
                if (a.Model != b.Model) return MathF.Abs(focal - a.Focal) <= MathF.Abs(focal - b.Focal) ? a : b;
                float t = LogLerp(a.Focal, b.Focal, focal);
                return new DistortionCalib
                {
                    Model = a.Model, Focal = focal,
                    K1 = Lerp(a.K1, b.K1, t), K2 = Lerp(a.K2, b.K2, t), K3 = Lerp(a.K3, b.K3, t),
                };
            }
        }
        return sorted[^1];
    }

    /// <summary>Nội suy vignetting theo tiêu cự (gần nhất theo focal; bỏ qua aperture/distance cho đơn giản).</summary>
    public static VignettingCalib? InterpolateVignetting(Lens lens, float focal)
    {
        var list = lens.Vignettings;
        if (list.Count == 0) return null;
        return list.OrderBy(v => MathF.Abs(v.Focal - focal)).First();
    }

    private static float LogLerp(float a, float b, float x)
    {
        if (a <= 0f || b <= 0f || a == b) return 0f;
        return (MathF.Log(x) - MathF.Log(a)) / (MathF.Log(b) - MathF.Log(a));
    }
    private static float Lerp(float a, float b, float t) => a + (b - a) * Math.Clamp(t, 0f, 1f);

    private static string Normalize(string s) => string.Join(' ', s.Trim().ToLowerInvariant()
        .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries));

    private static float F(string? s, float def = 0f)
        => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : def;
    private static float FAttr(XElement e, string name, float def = 0f)
        => F((string?)e.Attribute(name), def);
}
