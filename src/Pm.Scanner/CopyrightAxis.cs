using System.Text.RegularExpressions;

namespace Pm.Scanner;

// 從 WD14 character canonical 解析「作品(copyright)」。
// 與前端 tag-display.ts parseCharacter 的 work 判定一致:反覆剝離尾端 _(...),
// 最右側「非黑名單」群組 = 作品;畸形或全黑名單 → null。canonical 不變,本層只讀。
public static partial class CopyrightAxis
{
    // 限定詞黑名單(命中則歸造型/性別等,非作品)。與前端 NON_WORK_SUFFIX 同一份語意。
    private static readonly HashSet<string> NonWorkSuffix = new(StringComparer.Ordinal)
    {
        "male", "female", "young", "old", "aged_up", "child", "teenage", "adult",
        "alternate", "cosplay", "ghost", "human", "beast",
    };

    [GeneratedRegex(@"_\(([^()]*)\)$")]
    private static partial Regex SuffixRe();

    public static string? ParseWork(string canonical)
    {
        var rest = canonical ?? string.Empty;
        var groups = new List<string>();
        Match m;
        while ((m = SuffixRe().Match(rest)).Success)
        {
            groups.Insert(0, m.Groups[1].Value);   // 還原由左到右
            rest = rest[..m.Index];
        }
        if (rest.Length == 0) return null;          // 畸形:括號前無角色名
        for (var i = groups.Count - 1; i >= 0; i--)
            if (!NonWorkSuffix.Contains(groups[i]))
                return groups[i];                    // 最右側非黑名單 = 作品
        return null;                                 // 無括號 / 全黑名單
    }
}
