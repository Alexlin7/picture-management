using Microsoft.ML.OnnxRuntime;

namespace Pm.Ml;

public sealed class Wd14Tagger(IInferenceSessionFactory factory, Wd14Options options) : IWd14Tagger
{
    private InferenceSession? _session;
    private IReadOnlyList<Wd14Tag>? _tags;
    private string? _inputName;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (_session is not null) return;
        await _gate.WaitAsync(ct);
        try
        {
            if (_session is not null) return;
            var (modelPath, tagsPath) = await Wd14ModelProvider.EnsureAsync(options, ct);
            var session = factory.Create(modelPath);
            _inputName = session.InputMetadata.Keys.First();
            _tags = Wd14Tags.Parse(await File.ReadAllLinesAsync(tagsPath, ct));
            _session = session;
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<(string Name, string Kind, float Conf)>> TagAsync(
        string imageAbsPath, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);
        var tensor = Wd14Preprocess.ToTensor(imageAbsPath, options.Size);
        using var results = _session!.Run(new[] { NamedOnnxValue.CreateFromTensor(_inputName!, tensor) });
        var probs = results.First().AsEnumerable<float>().ToArray();
        return Wd14Postprocess.Select(probs, _tags!, options.GeneralThreshold, options.CharacterThreshold);
    }
}
