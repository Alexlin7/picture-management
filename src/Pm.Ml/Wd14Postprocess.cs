namespace Pm.Ml;

public static class Wd14Postprocess
{
    // danbooru/WD14 category → 我們的 tag.kind
    // 0=general, 3=copyright, 4=character, 9=rating(meta);其餘歸 general。
    // 註:實際 category 值以 selected_tags.csv 為準,必要時校正。
    public static string KindOf(int category) => category switch
    {
        4 => "character",
        3 => "copyright",
        9 => "meta",
        0 => "general",
        _ => "general",
    };

    public static IReadOnlyList<(string Name, string Kind, float Conf)> Select(
        IReadOnlyList<float> probs,
        IReadOnlyList<Wd14Tag> tags,
        float generalThreshold,
        float characterThreshold)
    {
        var result = new List<(string, string, float)>();
        var n = Math.Min(probs.Count, tags.Count);
        for (var i = 0; i < n; i++)
        {
            var t = tags[i];
            if (t.Category == 9) continue;                    // rating 不當一般標籤
            var thr = t.Category == 4 ? characterThreshold : generalThreshold;
            if (probs[i] >= thr)
                result.Add((t.Name, KindOf(t.Category), probs[i]));
        }
        return result;
    }
}
