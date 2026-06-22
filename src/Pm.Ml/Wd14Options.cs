namespace Pm.Ml;

public sealed class Wd14Options
{
    public string ModelDir { get; set; } = "models/wd14";
    public string ModelOnnxUrl { get; set; } =
        "https://huggingface.co/SmilingWolf/wd-vit-tagger-v3/resolve/main/model.onnx";
    public string TagsCsvUrl { get; set; } =
        "https://huggingface.co/SmilingWolf/wd-vit-tagger-v3/resolve/main/selected_tags.csv";
    public float GeneralThreshold { get; set; } = 0.35f;
    public float CharacterThreshold { get; set; } = 0.85f;
    public int Size { get; set; } = 448;
}
