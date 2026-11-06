using System.Text.Json;

namespace TempoKaraoke.Locales;

/// <summary>One language entry from localization/manifest.json.</summary>
public sealed class LocalizationManifestEntry
{
    public string Tag { get; init; } = string.Empty;

    public string File { get; init; } = string.Empty;

    public string NativeName { get; init; } = string.Empty;

    public string EnglishName { get; init; } = string.Empty;

    public bool Enabled { get; init; } = true;
}

/// <summary>The manifest is the single source of the supported language list.</summary>
public sealed class LocalizationManifest
{
    public int Version { get; init; } = 1;

    public string Fallback { get; init; } = LocalizationCatalog.DefaultLanguage;

    public IReadOnlyList<LocalizationManifestEntry> Languages { get; init; } = Array.Empty<LocalizationManifestEntry>();
}

/// <summary>A non-fatal problem found while loading or validating the catalog.</summary>
public sealed record LocalizationIssue(string Code, string Message, string? Language = null)
{
    public override string ToString() =>
        Language is null ? $"{Code}: {Message}" : $"{Code} ({Language}): {Message}";
}

public sealed class ManifestLoadResult
{
    public ManifestLoadResult(LocalizationManifest? manifest, IReadOnlyList<LocalizationIssue> issues, string path)
    {
        Manifest = manifest;
        Issues = issues;
        Path = path;
    }

    public LocalizationManifest? Manifest { get; }

    public IReadOnlyList<LocalizationIssue> Issues { get; }

    public string Path { get; }

    public bool IsValid => Manifest is not null;
}

/// <summary>Reads and structurally validates localization/manifest.json.</summary>
public static class LocalizationManifestLoader
{
    public const string FileName = "manifest.json";

    public static ManifestLoadResult Load(string directory)
    {
        var path = System.IO.Path.Combine(directory, FileName);
        var issues = new List<LocalizationIssue>();

        if (!File.Exists(path))
        {
            issues.Add(new LocalizationIssue("manifest_missing", $"Manifest not found: {path}"));
            return new ManifestLoadResult(null, issues, path);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(
                File.ReadAllText(path),
                new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        }
        catch (Exception ex)
        {
            issues.Add(new LocalizationIssue("manifest_invalid_json", ex.Message));
            return new ManifestLoadResult(null, issues, path);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                issues.Add(new LocalizationIssue("manifest_invalid_json", "Root element must be an object."));
                return new ManifestLoadResult(null, issues, path);
            }

            var root = document.RootElement;
            var version = root.TryGetProperty("version", out var versionElement) &&
                          versionElement.TryGetInt32(out var versionValue)
                ? versionValue
                : 1;

            var fallback = ReadString(root, "fallback") ?? LocalizationCatalog.DefaultLanguage;
            if (!LanguageTag.IsFullTag(fallback))
            {
                issues.Add(new LocalizationIssue(
                    "fallback_invalid",
                    $"Fallback '{fallback}' is not a full BCP 47 tag."));
            }

            var entries = new List<LocalizationManifestEntry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (!root.TryGetProperty("languages", out var languagesElement) ||
                languagesElement.ValueKind != JsonValueKind.Array)
            {
                issues.Add(new LocalizationIssue("languages_missing", "The 'languages' array is missing."));
            }
            else
            {
                var index = 0;
                foreach (var item in languagesElement.EnumerateArray())
                {
                    index++;
                    if (item.ValueKind != JsonValueKind.Object)
                    {
                        issues.Add(new LocalizationIssue(
                            "language_invalid",
                            $"Language entry #{index} must be an object."));
                        continue;
                    }

                    var tag = ReadString(item, "tag")?.Trim() ?? string.Empty;
                    var file = ReadString(item, "file")?.Trim() ?? string.Empty;
                    var nativeName = ReadString(item, "nativeName")?.Trim() ?? string.Empty;
                    var englishName = ReadString(item, "englishName")?.Trim() ?? string.Empty;
                    var enabled = !item.TryGetProperty("enabled", out var enabledElement) ||
                                  enabledElement.ValueKind != JsonValueKind.False;

                    if (!LanguageTag.IsFullTag(tag))
                    {
                        issues.Add(new LocalizationIssue(
                            "language_invalid_tag",
                            $"'{tag}' is not a full BCP 47 tag (expected e.g. de-DE, not de).",
                            tag));
                    }

                    if (string.IsNullOrWhiteSpace(nativeName))
                    {
                        issues.Add(new LocalizationIssue(
                            "language_missing_native_name",
                            $"Language '{tag}' has an empty nativeName.",
                            tag));
                    }

                    if (!IsSafeRelativeFile(file))
                    {
                        issues.Add(new LocalizationIssue(
                            "language_invalid_file",
                            $"Language '{tag}' has an invalid file reference '{file}'.",
                            tag));
                        continue;
                    }

                    if (!seen.Add(tag))
                    {
                        issues.Add(new LocalizationIssue(
                            "language_duplicate_tag",
                            $"Language tag '{tag}' is listed more than once.",
                            tag));
                        continue;
                    }

                    var fullPath = System.IO.Path.Combine(directory, file);
                    if (!File.Exists(fullPath))
                    {
                        issues.Add(new LocalizationIssue(
                            "language_missing_file",
                            $"File '{file}' for language '{tag}' does not exist.",
                            tag));
                    }

                    entries.Add(new LocalizationManifestEntry
                    {
                        Tag = tag,
                        File = file,
                        NativeName = nativeName,
                        EnglishName = englishName,
                        Enabled = enabled,
                    });
                }
            }

            var enabledTags = entries
                .Where(entry => entry.Enabled)
                .Select(entry => entry.Tag)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!enabledTags.Contains(fallback))
            {
                issues.Add(new LocalizationIssue(
                    "fallback_not_listed",
                    $"Fallback '{fallback}' is not an enabled language in the manifest.",
                    fallback));
            }

            var manifest = new LocalizationManifest
            {
                Version = version,
                Fallback = fallback,
                Languages = entries,
            };

            return new ManifestLoadResult(manifest, issues, path);
        }
    }

    internal static bool IsSafeRelativeFile(string file)
    {
        if (string.IsNullOrWhiteSpace(file) || System.IO.Path.IsPathRooted(file))
        {
            return false;
        }

        if (file.Contains("..", StringComparison.Ordinal) ||
            file.Contains(':', StringComparison.Ordinal) ||
            file.Contains('/', StringComparison.Ordinal) ||
            file.Contains('\\', StringComparison.Ordinal))
        {
            return false;
        }

        return file.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
