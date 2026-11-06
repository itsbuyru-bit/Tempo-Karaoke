using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace TempoKaraoke.Tests;

/// <summary>
/// MRT сопоставляет ресурсы x:Uid без учёта регистра. Если в разметке есть
/// «Ui_HostPIN» и «Ui_HostPin», первому достанется ключ .Header второго —
/// и приложение падает на загрузке страницы. Тест ловит такие пары.
/// </summary>
public sealed class XamlResourceUidTests
{
    [Fact]
    public void ResourceUidsDoNotDifferOnlyByCase()
    {
        var appDirectory = Path.Combine(FindRepositoryRoot(), "src", "TempoKaraoke");
        var pattern = new Regex("x:Uid=\"(?<uid>[^\"]+)\"", RegexOptions.Compiled);
        var usages = new List<(string File, string Uid)>();

        foreach (var file in Directory.EnumerateFiles(appDirectory, "*.xaml", SearchOption.AllDirectories))
        {
            var separator = Path.DirectorySeparatorChar;
            if (file.Contains($"{separator}obj{separator}") || file.Contains($"{separator}bin{separator}"))
            {
                continue;
            }

            foreach (Match match in pattern.Matches(File.ReadAllText(file)))
            {
                usages.Add((Path.GetFileName(file), match.Groups["uid"].Value));
            }
        }

        var collisions = usages
            .GroupBy(usage => usage.Uid, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Select(usage => usage.Uid).Distinct(StringComparer.Ordinal).Count() > 1)
            .Select(group => string.Join(", ", group.Select(usage => $"{usage.Uid} ({usage.File})")))
            .ToList();

        Assert.True(
            collisions.Count == 0,
            "x:Uid отличаются только регистром; MRT применит ресурсы не тому элементу: " +
            string.Join("; ", collisions));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TempoKaraoke.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Не найден корень репозитория (TempoKaraoke.slnx).");
    }
}
