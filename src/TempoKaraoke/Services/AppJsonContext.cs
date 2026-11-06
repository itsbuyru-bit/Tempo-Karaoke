using System.Collections.Generic;
using System.Text.Json.Serialization;
using TempoKaraoke.Models;
using TempoKaraoke.Security;

namespace TempoKaraoke.Services;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(SettingsData))]
[JsonSerializable(typeof(SecuritySettings))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(List<Playlist>))]
[JsonSerializable(typeof(Playlist))]
[JsonSerializable(typeof(List<QueueEntry>))]
[JsonSerializable(typeof(LibraryActivityData))]
[JsonSerializable(typeof(List<RemoteClientAlias>))]
[JsonSerializable(typeof(List<RemoteClientHistory>))]
[JsonSerializable(typeof(GitHubRelease))]
internal partial class AppJsonContext : JsonSerializerContext
{
}
