namespace Pm.Scanner;

public static class PathTagDefaults
{
    public static string Suggest(string segment)
    {
        if (segment == "我不知道") return "ignore";
        // 四位數且落在合理年份範圍才當年份;像「2434」這種社群編號仍當一般 tag
        if (segment.Length == 4 && segment.All(char.IsDigit)
            && int.TryParse(segment, out var year) && year is >= 1900 and <= 2099)
            return "meta_year";  // 2023/2024…
        return "map_to_tag";
    }
}
