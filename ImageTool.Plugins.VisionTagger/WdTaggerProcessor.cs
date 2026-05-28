using System.Globalization;
using System.IO;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ImageTool.Plugins.VisionTagger;

/// <summary>
/// WD ViT Tagger v3 wrapper. Input 448x448 BGR float, padded white.
/// Output: 3 nhóm tag (rating/general/character). Lọc theo threshold.
/// </summary>
public class WdTaggerProcessor : IDisposable
{
    private readonly InferenceSession _session;
    private readonly List<TagInfo> _tags;
    private readonly int _inputSize;

    public WdTaggerProcessor(string modelPath, string tagsCsvPath)
    {
        var opts = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
        };
        try { opts.AppendExecutionProvider_DML(0); } catch { /* fallback CPU */ }
        _session = new InferenceSession(modelPath, opts);

        var input = _session.InputMetadata.Values.First();
        // shape: [1, H, W, 3] với H=W=448 cho v3
        _inputSize = input.Dimensions.Length >= 2 && input.Dimensions[1] > 0 ? input.Dimensions[1] : 448;

        _tags = LoadTagsCsv(tagsCsvPath);
    }

    public TagResult Run(string imagePath, float generalThreshold = 0.35f, float characterThreshold = 0.85f)
    {
        using var img = Image.Load<Rgba32>(imagePath);
        return RunInternal(img, generalThreshold, characterThreshold);
    }

    private TagResult RunInternal(Image<Rgba32> img, float gThr, float cThr)
    {
        // Pad about white to square, resize to inputSize
        int sz = Math.Max(img.Width, img.Height);
        using var square = new Image<Rgba32>(sz, sz, new Rgba32(255, 255, 255, 255));
        square.Mutate(c => c.DrawImage(img, new Point((sz - img.Width) / 2, (sz - img.Height) / 2), 1f));
        square.Mutate(c => c.Resize(_inputSize, _inputSize, KnownResamplers.Bicubic));

        int n = _inputSize * _inputSize;
        var data = new float[1 * _inputSize * _inputSize * 3];
        int idx = 0;
        square.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < _inputSize; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < _inputSize; x++)
                {
                    var px = row[x];
                    // BGR float, no normalize
                    data[idx++] = px.B;
                    data[idx++] = px.G;
                    data[idx++] = px.R;
                }
            }
        });

        var tensor = new DenseTensor<float>(data, new[] { 1, _inputSize, _inputSize, 3 });
        var inputName = _session.InputMetadata.Keys.First();
        using var results = _session.Run(new[] { NamedOnnxValue.CreateFromTensor(inputName, tensor) });
        var output = results.First().AsTensor<float>().ToArray();

        var rating = new List<(string Tag, float Score)>();
        var general = new List<(string Tag, float Score)>();
        var character = new List<(string Tag, float Score)>();

        int len = Math.Min(output.Length, _tags.Count);
        for (int i = 0; i < len; i++)
        {
            var t = _tags[i];
            var s = output[i];
            switch (t.Category)
            {
                case 9: rating.Add((t.Name, s)); break;
                case 0:
                    if (s >= gThr) general.Add((t.Name, s));
                    break;
                case 4:
                    if (s >= cThr) character.Add((t.Name, s));
                    break;
            }
        }

        rating = rating.OrderByDescending(x => x.Score).ToList();
        general = general.OrderByDescending(x => x.Score).ToList();
        character = character.OrderByDescending(x => x.Score).ToList();

        return new TagResult(rating, general, character);
    }

    private static List<TagInfo> LoadTagsCsv(string path)
    {
        var list = new List<TagInfo>();
        var lines = File.ReadAllLines(path);
        for (int i = 1; i < lines.Length; i++)
        {
            var parts = SplitCsv(lines[i]);
            if (parts.Length < 3) { list.Add(new TagInfo("", 0)); continue; }
            int category = int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var c) ? c : 0;
            list.Add(new TagInfo(parts[1], category));
        }
        return list;
    }

    private static string[] SplitCsv(string line)
    {
        // Lightweight CSV split với hỗ trợ trường có ngoặc kép
        var result = new List<string>();
        var sb = new System.Text.StringBuilder();
        bool inQuote = false;
        foreach (var ch in line)
        {
            if (ch == '"') { inQuote = !inQuote; continue; }
            if (ch == ',' && !inQuote) { result.Add(sb.ToString()); sb.Clear(); continue; }
            sb.Append(ch);
        }
        result.Add(sb.ToString());
        return result.ToArray();
    }

    public void Dispose() => _session.Dispose();

    private record TagInfo(string Name, int Category);
}

public record TagResult(
    List<(string Tag, float Score)> Rating,
    List<(string Tag, float Score)> General,
    List<(string Tag, float Score)> Character
);
