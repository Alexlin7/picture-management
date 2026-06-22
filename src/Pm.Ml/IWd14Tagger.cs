namespace Pm.Ml;

public interface IWd14Tagger
{
    Task<IReadOnlyList<(string Name, string Kind, float Conf)>> TagAsync(
        string imageAbsPath, CancellationToken ct = default);
}
