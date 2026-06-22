namespace Pm.Ml;

// selected_tags.csv 欄位:tag_id,name,category,count(首列為標頭)
public static class Wd14Tags
{
    public static IReadOnlyList<Wd14Tag> Parse(IEnumerable<string> csvLines)
    {
        var list = new List<Wd14Tag>();
        var first = true;
        foreach (var line in csvLines)
        {
            if (first) { first = false; continue; }          // 跳標頭
            if (string.IsNullOrWhiteSpace(line)) continue;
            var cols = line.Split(',');
            if (cols.Length < 3) continue;
            var id = long.TryParse(cols[0], out var x) ? x : 0;
            var name = cols[1];
            var cat = int.TryParse(cols[2], out var c) ? c : 0;
            list.Add(new Wd14Tag(id, name, cat));
        }
        return list;
    }
}
