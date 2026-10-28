namespace TempoKaraoke.Models;

public sealed class FolderInfo
{
    public FolderInfo(string name, int count)
    {
        Name = name;
        Count = count;
    }

    public string Name { get; }

    public int Count { get; }

    public string Label => $"{Count} видео";
}
