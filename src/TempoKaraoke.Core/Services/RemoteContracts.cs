using System.Collections.Generic;
using System.Threading.Tasks;

namespace TempoKaraoke.Services;

public sealed record RemoteQueueItem(
    string Path,
    string Title,
    string Subtitle,
    string Duration,
    string Folder,
    string Requester,
    string RequesterId);

public sealed record RemoteSearchItem(
    string Path,
    string Title,
    string Subtitle,
    string Duration);

public sealed record RemoteSearchResponse(
    IReadOnlyList<RemoteSearchItem> Items,
    bool NeedsClarification);

public sealed record RemotePlaylistItem(
    string Id,
    string Name,
    int Count);

public sealed record RemoteSnapshot(
    string Title,
    string Subtitle,
    bool IsPlaying,
    long PositionMs,
    long LengthMs,
    int Volume,
    int CurrentIndex,
    IReadOnlyList<RemoteQueueItem> Queue,
    bool IsShuffle = false,
    string RepeatMode = "none");

public sealed record RemoteGuestRequest(
    string Id,
    string Path,
    string Title,
    string Requester,
    string RequesterId,
    long CreatedUtcTicks);

public sealed record RemoteAdminSettings(
    bool CountdownEnabled,
    int CountdownSeconds,
    int MaxRequestsPerGuest,
    int MaxPendingRequests,
    string Theme = "system",
    string Language = "en-US");

public sealed record RemoteRequestDecision(
    string Id,
    string Action);

public sealed record RemoteArchivedRequest(
    string Id,
    string Path,
    string Title,
    string Requester,
    string Status,
    long CreatedUtcTicks,
    long ClosedUtcTicks);

public sealed record RemoteAuthResponse(
    bool Ok,
    string Role);

/// <summary>
/// Мост между HTTP-сервером пульта и UI-потоком приложения.
/// Реализация обязана маршалить вызовы в DispatcherQueue и не блокировать поток.
/// </summary>
public interface IRemoteHost
{
    Task<RemoteSnapshot> GetSnapshotAsync();

    Task<IReadOnlyList<RemoteSearchItem>> SearchAsync(string query);

    Task<IReadOnlyList<string>> SuggestAsync(string query);

    Task<bool> EnqueueAsync(string path, string? requesterId, string? requesterName);

    Task<bool> RemoveFromQueueAsync(int index);

    Task<bool> MoveQueueItemAsync(int from, int to);

    Task<bool> ControlAsync(string action, double value);

    Task<bool> PlayNowAsync(string trackId);

    Task<bool> ClearQueueAsync();

    Task<IReadOnlyList<RemotePlaylistItem>> GetPlaylistsAsync();

    Task<bool> AppendPlaylistAsync(string playlistId);

    Task<IReadOnlyList<RemoteGuestRequest>> GetRequestsAsync();

    Task<IReadOnlyList<RemoteArchivedRequest>> GetRequestArchiveAsync();

    Task<bool> ClearRequestArchiveAsync();

    Task<bool> CreateNextRequestAsync(string path, string? requesterId, string? requesterName);

    Task<bool> ResolveRequestAsync(string requestId, string action);

    Task<RemoteAdminSettings> GetAdminSettingsAsync();

    Task<bool> UpdateAdminSettingsAsync(RemoteAdminSettings settings);
}
