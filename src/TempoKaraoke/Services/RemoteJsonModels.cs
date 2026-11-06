using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TempoKaraoke.Services;

public sealed record StateResponse(
    RemoteSnapshot State,
    bool CanControl,
    bool CanAdmin,
    string ClientId,
    string AliasName,
    string FallbackName);

public sealed record OkResponse(bool Ok);

public sealed class QueueRequest
{
    public string Path { get; set; } = string.Empty;

    public string? RequesterName { get; set; }
}

public sealed class RemoveRequest
{
    public int Index { get; set; }
}

public sealed class MoveRequest
{
    public int From { get; set; }

    public int To { get; set; }
}

public sealed class ControlRequest
{
    public string Action { get; set; } = string.Empty;

    public double Value { get; set; }
}

public sealed class HostRequest
{
    public string Pin { get; set; } = string.Empty;
}

public sealed class AdminRequest
{
    public string Pin { get; set; } = string.Empty;
}

public sealed class RequestCreate
{
    public string Path { get; set; } = string.Empty;

    public string? RequesterName { get; set; }
}

public sealed class RequestResolve
{
    public string Id { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;
}

public sealed class AdminSettingsRequest
{
    public bool CountdownEnabled { get; set; }

    public int CountdownSeconds { get; set; } = 10;

    public int MaxRequestsPerGuest { get; set; } = 2;

    public int MaxPendingRequests { get; set; } = 20;

    public string Theme { get; set; } = "system";

    public string Language { get; set; } = "en-US";
}

public sealed class NameRequest
{
    public string Name { get; set; } = string.Empty;
}

public sealed class PlayNowRequest
{
    public string TrackId { get; set; } = string.Empty;
}

public sealed class PlaylistAddRequest
{
    public string Id { get; set; } = string.Empty;
}

public sealed class ClientErrorRequest
{
    public string Message { get; set; } = string.Empty;
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(StateResponse))]
[JsonSerializable(typeof(OkResponse))]
[JsonSerializable(typeof(RemoteSnapshot))]
[JsonSerializable(typeof(RemoteQueueItem))]
[JsonSerializable(typeof(RemoteGuestRequest))]
[JsonSerializable(typeof(List<RemoteGuestRequest>))]
[JsonSerializable(typeof(RemoteArchivedRequest))]
[JsonSerializable(typeof(List<RemoteArchivedRequest>))]
[JsonSerializable(typeof(RemoteAuthResponse))]
[JsonSerializable(typeof(RemoteAdminSettings))]
[JsonSerializable(typeof(RemoteRequestDecision))]
[JsonSerializable(typeof(RemoteSearchItem))]
[JsonSerializable(typeof(List<RemoteSearchItem>))]
[JsonSerializable(typeof(RemotePlaylistItem))]
[JsonSerializable(typeof(List<RemotePlaylistItem>))]
[JsonSerializable(typeof(QueueRequest))]
[JsonSerializable(typeof(RemoveRequest))]
[JsonSerializable(typeof(MoveRequest))]
[JsonSerializable(typeof(ControlRequest))]
[JsonSerializable(typeof(HostRequest))]
[JsonSerializable(typeof(AdminRequest))]
[JsonSerializable(typeof(RequestCreate))]
[JsonSerializable(typeof(RequestResolve))]
[JsonSerializable(typeof(AdminSettingsRequest))]
[JsonSerializable(typeof(NameRequest))]
[JsonSerializable(typeof(PlayNowRequest))]
[JsonSerializable(typeof(PlaylistAddRequest))]
[JsonSerializable(typeof(ClientErrorRequest))]
internal partial class RemoteJsonContext : JsonSerializerContext
{
}
