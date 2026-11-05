namespace TempoKaraoke.Models;

/// <summary>One segment of the video-folder breadcrumb.</summary>
public sealed class FolderCrumb
{
    public FolderCrumb(string name, string path)
    {
        Name = name;
        Path = path;
    }

    public string Name { get; }

    public string Path { get; }

    public override string ToString() => Name;
}
