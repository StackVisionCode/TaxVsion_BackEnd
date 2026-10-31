using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace TaxVision.ContentScanner.Scanning;

/// <summary>Puntúa un cuadro RGB (lado = InputSize) con la probabilidad de contenido sexual (0..1).</summary>
public interface INsfwClassifier
{
    bool IsReady { get; }

    float Score(ReadOnlySpan<byte> rgb);
}

/// <summary>
/// Clasificador ONNX (ViT, entrada 384×384 normalizada). La sesión se crea una vez y es segura para llamadas
/// concurrentes; si el modelo falta, el servicio arranca pero `/health/ready` responde 503.
/// </summary>
public sealed class OnnxNsfwClassifier : INsfwClassifier, IDisposable
{
    private readonly ContentScannerOptions _options;
    private readonly InferenceSession? _session;
    private readonly string _inputName = string.Empty;

    public OnnxNsfwClassifier(IOptions<ContentScannerOptions> options, ILogger<OnnxNsfwClassifier> logger)
    {
        _options = options.Value;
        if (!File.Exists(_options.ModelPath))
        {
            logger.LogError("Content scanner model not found at {ModelPath}", _options.ModelPath);
            return;
        }
        var sessionOptions = new Microsoft.ML.OnnxRuntime.SessionOptions
        {
            IntraOpNumThreads = _options.InferenceThreads,
            InterOpNumThreads = 1,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
        };
        _session = new InferenceSession(_options.ModelPath, sessionOptions);
        _inputName = _session.InputMetadata.Keys.First();
    }

    public bool IsReady => _session is not null;

    public float Score(ReadOnlySpan<byte> rgb)
    {
        if (_session is null)
            throw new InvalidOperationException("Content scanner model is not loaded.");
        var size = _options.InputSize;
        var input = new DenseTensor<float>(Preprocess(rgb, size, _options.Mean, _options.Std), [1, 3, size, size]);
        using var results = _session.Run([NamedOnnxValue.CreateFromTensor(_inputName, input)]);
        var logits = results[0].AsEnumerable<float>().ToArray();
        return Softmax(logits)[_options.NsfwLabelIndex];
    }

    /// <summary>RGB intercalado (HWC, bytes) → tensor planar (CHW) normalizado: (x/255 − mean) / std.</summary>
    internal static float[] Preprocess(ReadOnlySpan<byte> rgb, int size, IReadOnlyList<float> mean, IReadOnlyList<float> std)
    {
        var plane = size * size;
        if (rgb.Length != plane * 3)
            throw new ArgumentException($"Expected {plane * 3} bytes for a {size}x{size} RGB frame.", nameof(rgb));
        var data = new float[plane * 3];
        for (var i = 0; i < plane; i++)
        {
            for (var c = 0; c < 3; c++)
                data[c * plane + i] = (rgb[i * 3 + c] / 255f - mean[c]) / std[c];
        }
        return data;
    }

    internal static float[] Softmax(IReadOnlyList<float> logits)
    {
        var max = logits.Max();
        var exp = logits.Select(l => MathF.Exp(l - max)).ToArray();
        var sum = exp.Sum();
        return exp.Select(e => e / sum).ToArray();
    }

    public void Dispose() => _session?.Dispose();
}
