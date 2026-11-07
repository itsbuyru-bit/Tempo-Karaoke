using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QRCoder;
using TempoKaraoke.Security;
using Windows.Networking.Connectivity;

namespace TempoKaraoke.Services;

/// <summary>
/// Локальный HTTP/WebSocket-сервер пульта. Работает только в локальной сети,
/// доступ защищён случайным токеном из QR-кода, управление — PIN ведущего.
/// </summary>
public sealed class RemoteServer : IAsyncDisposable
{
    private const string ClientCookie = "tempo_client";
    private const int MaxPinAttempts = 5;
    private const int MaxSearchLength = 120;
    private static readonly TimeSpan PinLockout = TimeSpan.FromMinutes(5);

    private readonly IRemoteHost _host;
    private readonly RemoteClientDirectory _clients;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _startLock = new(1, 1);
    private readonly Dictionary<WebSocket, ClientSocket> _sockets = new();
    private readonly object _socketLock = new();
    private readonly ConcurrentDictionary<string, bool> _hostClients = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, bool> _adminClients = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, bool> _bannedClients = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _fallbackNames = new(StringComparer.Ordinal);
    private readonly PinAttempt _pinAttempt = new();
    private readonly PinAttempt _adminPinAttempt = new();
    private readonly PinService _pins;
    private int _guestCounter;
    private WebApplication? _app;
    private Task? _broadcast;
    private bool _disposed;

    public RemoteServer(IRemoteHost host, RemoteClientDirectory clients)
    {
        _host = host;
        _clients = clients;
        Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        _pins = new PinService(
            RandomNumberGenerator.GetInt32(1000, 10000).ToString(),
            RandomNumberGenerator.GetInt32(1000, 10000).ToString());
    }

    public string Token { get; }

    /// <summary>PIN ведущего или маска, если настроен постоянный PIN.</summary>
    public string Pin => _pins.DisplayHostPin;

    /// <summary>PIN администратора или маска, если настроен постоянный PIN.</summary>
    public string AdminPin => _pins.DisplayAdminPin;

    public bool HostPinIsPersistent => _pins.HostUsesPersistentPin;

    public int Port { get; private set; }

    public string Url { get; private set; } = string.Empty;

    public string QrPath { get; private set; } = string.Empty;

    /// <summary>QR as an in-memory PNG, so the remote UI works without writable storage.</summary>
    public byte[] QrPng { get; private set; } = Array.Empty<byte>();

    public bool IsRunning => _app is not null;

    public RemoteClientDirectory Clients => _clients;

    /// <summary>Сколько телефонов-пультов подключено прямо сейчас.</summary>
    public int ClientCount
    {
        get
        {
            lock (_socketLock)
            {
                return _sockets.Count;
            }
        }
    }

    /// <summary>Последняя активность любого пульта (запрос или WebSocket).</summary>
    public DateTime LastClientActivityUtc { get; private set; } = DateTime.UtcNow;

    public async Task StartAsync()
    {
        if (_app is not null || _disposed)
        {
            return;
        }

        _clients.DisconnectAll();

        await _startLock.WaitAsync();
        try
        {
            if (_app is not null || _disposed)
            {
                return;
            }

            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Any, 0));
            var app = builder.Build();
            app.UseWebSockets(new WebSocketOptions
            {
                // Pings keep NAT/router mappings alive while the phone is idle.
                KeepAliveInterval = TimeSpan.FromSeconds(20),
            });

            app.MapGet("/appicon.png", async context =>
            {
                var path = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon-192.png");
                if (!File.Exists(path))
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }

                context.Response.ContentType = "image/png";
                context.Response.Headers["Cache-Control"] = "public,max-age=86400";
                await context.Response.SendFileAsync(path);
            });

            app.MapGet("/locale-manifest.json", async context =>
            {
                // Prefer the editable localization folder next to the app. This
                // lets a new language appear after an app restart without a rebuild;
                // the generated public copy is only a fallback.
                var path = ResolveLocaleAsset("manifest.json");
                if (path is null)
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }

                context.Response.ContentType = "application/json; charset=utf-8";
                context.Response.Headers["Cache-Control"] = "no-cache";
                await context.Response.SendFileAsync(path);
            });

            app.MapGet("/locales/{lang}.json", async context =>
            {
                var lang = context.Request.RouteValues["lang"]?.ToString() ?? string.Empty;
                // Only full BCP 47 tags can be served; this also blocks path traversal.
                if (!lang.All(character => char.IsLetterOrDigit(character) || character == '-') ||
                    !lang.Contains('-', StringComparison.Ordinal) ||
                    lang.Length > 24)
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }

                var path = ResolveLocaleAsset($"locales/{lang}.json")
                    ?? ResolveLocaleAsset($"{lang}.json");
                if (path is null)
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }

                context.Response.ContentType = "application/json; charset=utf-8";
                context.Response.Headers["Cache-Control"] = "no-cache";
                await context.Response.SendFileAsync(path);
            });

            app.MapGet("/", async context =>
            {
                var clientId = await PrepareClientAsync(context, requireHost: false);
                if (clientId is null)
                {
                    return;
                }

                context.Response.ContentType = "text/html; charset=utf-8";
                context.Response.Headers["Cache-Control"] = "no-store";
                await context.Response.WriteAsync(RemotePage.Html);
            });

            app.MapGet("/api/state", async context =>
            {
                var clientId = await PrepareClientAsync(context, requireHost: false);
                if (clientId is null)
                {
                    return;
                }

                var state = await _host.GetSnapshotAsync();
                await WriteJsonAsync(
                    context,
                    new StateResponse(
                        state,
                        _hostClients.ContainsKey(clientId),
                        _adminClients.ContainsKey(clientId),
                        clientId,
                        _clients.GetAliasName(clientId) ?? string.Empty,
                        _clients.GetFallbackName(clientId) ?? string.Empty),
                    RemoteJsonContext.Default.StateResponse);
            });

            app.MapPost("/api/client-error", async context =>
            {
                var clientId = await PrepareClientAsync(context, requireHost: false);
                if (clientId is null)
                {
                    return;
                }

                var request = await ReadJsonAsync(context, RemoteJsonContext.Default.ClientErrorRequest);
                if (request is not null && !string.IsNullOrWhiteSpace(request.Message))
                {
                    var message = request.Message.Length > 400
                        ? request.Message[..400]
                        : request.Message;
                    LogService.Warn($"Web remote error: {message}", "web");
                }

                await WriteJsonAsync(context, new OkResponse(true), RemoteJsonContext.Default.OkResponse);
            });

            app.MapGet("/api/search", async context =>
            {
                var clientId = await PrepareClientAsync(context, requireHost: false);
                if (clientId is null)
                {
                    return;
                }

                var query = context.Request.Query["q"].ToString();
                if (query.Length > MaxSearchLength)
                {
                    query = query[..MaxSearchLength];
                }

                var results = await _host.SearchAsync(query);
                await WriteJsonAsync(
                    context,
                    new RemoteSearchResponse(
                        new List<RemoteSearchItem>(results),
                        SearchService.IsStopWordOnlyQuery(query) && results.Count == 0),
                    RemoteJsonContext.Default.RemoteSearchResponse);
            });

            app.MapGet("/api/suggest", async context =>
            {
                var clientId = await PrepareClientAsync(context, requireHost: false);
                if (clientId is null)
                {
                    return;
                }

                var query = context.Request.Query["q"].ToString();
                var suggestions = await _host.SuggestAsync(query);
                await WriteJsonAsync(
                    context,
                    new List<string>(suggestions),
                    RemoteJsonContext.Default.ListString);
            });

            app.MapPost("/api/queue", async context =>
            {
                var clientId = await PrepareClientAsync(context, requireHost: false);
                if (clientId is null)
                {
                    return;
                }

                var request = await ReadJsonAsync(context, RemoteJsonContext.Default.QueueRequest);
                var ok = request is not null &&
                         !string.IsNullOrWhiteSpace(request.Path) &&
                         await _host.EnqueueAsync(request.Path, clientId, request.RequesterName);
                await WriteJsonAsync(context, new OkResponse(ok), RemoteJsonContext.Default.OkResponse);
                if (ok)
                {
                    NotifyStateChanged();
                }
            });

            app.MapPost("/api/remove", async context =>
            {
                var clientId = await PrepareClientAsync(context, requireHost: true);
                if (clientId is null)
                {
                    return;
                }

                var request = await ReadJsonAsync(context, RemoteJsonContext.Default.RemoveRequest);
                var ok = request is not null && await _host.RemoveFromQueueAsync(request.Index);
                await WriteJsonAsync(context, new OkResponse(ok), RemoteJsonContext.Default.OkResponse);
                if (ok)
                {
                    NotifyStateChanged();
                }
            });

            app.MapPost("/api/move", async context =>
            {
                var clientId = await PrepareClientAsync(context, requireHost: true);
                if (clientId is null)
                {
                    return;
                }

                var request = await ReadJsonAsync(context, RemoteJsonContext.Default.MoveRequest);
                var ok = request is not null && await _host.MoveQueueItemAsync(request.From, request.To);
                await WriteJsonAsync(context, new OkResponse(ok), RemoteJsonContext.Default.OkResponse);
                if (ok)
                {
                    NotifyStateChanged();
                }
            });

            app.MapPost("/api/control", async context =>
            {
                var clientId = await PrepareClientAsync(context, requireHost: true);
                if (clientId is null)
                {
                    return;
                }

                var request = await ReadJsonAsync(context, RemoteJsonContext.Default.ControlRequest);
                var ok = request is not null && await _host.ControlAsync(request.Action, request.Value);
                await WriteJsonAsync(context, new OkResponse(ok), RemoteJsonContext.Default.OkResponse);
                if (ok)
                {
                    NotifyStateChanged();
                }
            });

            app.MapPost("/api/auth", async context =>
            {
                var clientId = await PrepareClientAsync(context, requireHost: false);
                if (clientId is null)
                {
                    return;
                }

                var request = await ReadJsonAsync(context, RemoteJsonContext.Default.HostRequest);
                var role = string.Empty;
                if (request is not null)
                {
                    if (VerifyAdminPinQuiet(request.Pin))
                    {
                        _adminClients[clientId] = true;
                        _hostClients[clientId] = true;
                        _clients.SetHost(clientId, true);
                        role = "admin";
                    }
                    else if (TryVerifyPin(clientId, request.Pin))
                    {
                        _hostClients[clientId] = true;
                        _clients.SetHost(clientId, true);
                        role = "host";
                    }
                    else
                    {
                        // Count the failed attempt against the administrator
                        // lockout only when neither role matched. A successful
                        // host sign-in must not consume admin attempts.
                        TryVerifyAdminPin(clientId, request.Pin);
                    }
                }

                if (role.Length == 0)
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }

                await WriteJsonAsync(
                    context,
                    new RemoteAuthResponse(true, role),
                    RemoteJsonContext.Default.RemoteAuthResponse);
                NotifyStateChanged();
            });

            app.MapPost("/api/host", async context =>
            {
                var clientId = await PrepareClientAsync(context, requireHost: false);
                if (clientId is null)
                {
                    return;
                }

                var request = await ReadJsonAsync(context, RemoteJsonContext.Default.HostRequest);
                var ok = request is not null && TryVerifyPin(clientId, request.Pin);
                if (ok)
                {
                    _hostClients[clientId] = true;
                    _clients.SetHost(clientId, true);
                }

                await WriteJsonAsync(context, new OkResponse(ok), RemoteJsonContext.Default.OkResponse);
                if (ok)
                {
                    NotifyStateChanged();
                }
            });

            app.MapPost("/api/admin", async context =>
            {
                var clientId = await PrepareClientAsync(context, requireHost: false);
                if (clientId is null)
                {
                    return;
                }

                var request = await ReadJsonAsync(context, RemoteJsonContext.Default.AdminRequest);
                var ok = request is not null && TryVerifyAdminPin(clientId, request.Pin);
                if (ok)
                {
                    _adminClients[clientId] = true;
                    _hostClients[clientId] = true;
                    _clients.SetHost(clientId, true);
                }

                await WriteJsonAsync(context, new OkResponse(ok), RemoteJsonContext.Default.OkResponse);
                if (ok)
                {
                    NotifyStateChanged();
                }
            });

            app.MapPost("/api/admin/exit", async context =>
            {
                var clientId = await PrepareClientAsync(context, requireHost: false);
                if (clientId is null)
                {
                    return;
                }

                _adminClients.TryRemove(clientId, out _);
                await WriteJsonAsync(context, new OkResponse(true), RemoteJsonContext.Default.OkResponse);
                NotifyStateChanged();
            });

            app.MapGet("/api/admin/settings", async context =>
            {
                var clientId = await PrepareClientAsync(context, requireHost: false);
                if (clientId is null)
                {
                    return;
                }

                if (!_adminClients.ContainsKey(clientId))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }

                var settings = await _host.GetAdminSettingsAsync();
                await WriteJsonAsync(context, settings, RemoteJsonContext.Default.RemoteAdminSettings);
            });

            app.MapPost("/api/admin/settings", async context =>
            {
                var clientId = await PrepareClientAsync(context, requireHost: false);
                if (clientId is null)
                {
                    return;
                }

                if (!_adminClients.ContainsKey(clientId))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }

                var request = await ReadJsonAsync(context, RemoteJsonContext.Default.AdminSettingsRequest);
                var ok = request is not null &&
                         await _host.UpdateAdminSettingsAsync(new RemoteAdminSettings(
                             request.CountdownEnabled,
                             request.CountdownSeconds,
                             request.MaxRequestsPerGuest,
                             request.MaxPendingRequests,
                             request.Theme,
                             request.Language));
                await WriteJsonAsync(context, new OkResponse(ok), RemoteJsonContext.Default.OkResponse);
                if (ok)
                {
                    NotifyStateChanged();
                }
            });

            app.MapGet("/api/requests", async context =>
            {
                var clientId = await PrepareClientAsync(context, requireHost: false);
                if (clientId is null)
                {
                    return;
                }

                var requests = await _host.GetRequestsAsync();
                if (!_hostClients.ContainsKey(clientId) && !_adminClients.ContainsKey(clientId))
                {
                    requests = requests
                        .Where(request => string.Equals(request.RequesterId, clientId, StringComparison.Ordinal))
                        .ToList();
                }

                await WriteJsonAsync(
                    context,
                    new List<RemoteGuestRequest>(requests),
                    RemoteJsonContext.Default.ListRemoteGuestRequest);
            });

            app.MapPost("/api/requests", async context =>
            {
                var clientId = await PrepareClientAsync(context, requireHost: false);
                if (clientId is null)
                {
                    return;
                }

                var request = await ReadJsonAsync(context, RemoteJsonContext.Default.RequestCreate);
                var ok = request is not null &&
                         !string.IsNullOrWhiteSpace(request.Path) &&
                         await _host.CreateNextRequestAsync(request.Path, clientId, request.RequesterName);
                await WriteJsonAsync(context, new OkResponse(ok), RemoteJsonContext.Default.OkResponse);
                if (ok)
                {
                    NotifyStateChanged();
                }
            });

            app.MapPost("/api/requests/resolve", async context =>
            {
                var clientId = await PrepareClientAsync(context, requireHost: false);
                if (clientId is null)
                {
                    return;
                }

                if (!_hostClients.ContainsKey(clientId) && !_adminClients.ContainsKey(clientId))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }

                var request = await ReadJsonAsync(context, RemoteJsonContext.Default.RequestResolve);
                var ok = request is not null &&
                         !string.IsNullOrWhiteSpace(request.Id) &&
                         await _host.ResolveRequestAsync(request.Id, request.Action);
                await WriteJsonAsync(context, new OkResponse(ok), RemoteJsonContext.Default.OkResponse);
                if (ok)
                {
                    NotifyStateChanged();
                }
            });

            app.MapGet("/api/requests/archive", async context =>
            {
                var clientId = await PrepareClientAsync(context, requireHost: false);
                if (clientId is null)
                {
                    return;
                }

                if (!_hostClients.ContainsKey(clientId) && !_adminClients.ContainsKey(clientId))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }

                var archive = await _host.GetRequestArchiveAsync();
                await WriteJsonAsync(
                    context,
                    new List<RemoteArchivedRequest>(archive),
                    RemoteJsonContext.Default.ListRemoteArchivedRequest);
            });

            app.MapPost("/api/requests/archive/clear", async context =>
            {
                var clientId = await PrepareClientAsync(context, requireHost: false);
                if (clientId is null)
                {
                    return;
                }

                if (!_hostClients.ContainsKey(clientId) && !_adminClients.ContainsKey(clientId))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }

                var ok = await _host.ClearRequestArchiveAsync();
                await WriteJsonAsync(context, new OkResponse(ok), RemoteJsonContext.Default.OkResponse);
                if (ok)
                {
                    NotifyStateChanged();
                }
            });

            app.MapPost("/api/name", async context =>
            {
                var clientId = await PrepareClientAsync(context, requireHost: false);
                if (clientId is null)
                {
                    return;
                }

                var request = await ReadJsonAsync(context, RemoteJsonContext.Default.NameRequest);
                var ok = request is not null &&
                         !string.IsNullOrWhiteSpace(request.Name) &&
                         _clients.SetClientName(clientId, request.Name);
                await WriteJsonAsync(context, new OkResponse(ok), RemoteJsonContext.Default.OkResponse);
                if (ok)
                {
                    NotifyStateChanged();
                }
            });

            app.MapPost("/api/host/exit", async context =>
            {
                var clientId = await PrepareClientAsync(context, requireHost: true);
                if (clientId is null)
                {
                    return;
                }

                _hostClients.TryRemove(clientId, out _);
                _clients.SetHost(clientId, false);
                await WriteJsonAsync(context, new OkResponse(true), RemoteJsonContext.Default.OkResponse);
                NotifyStateChanged();
            });

            app.MapPost("/api/queue/play-now", async context =>
            {
                var clientId = await PrepareClientAsync(context, requireHost: true);
                if (clientId is null)
                {
                    return;
                }

                var request = await ReadJsonAsync(context, RemoteJsonContext.Default.PlayNowRequest);
                var ok = request is not null &&
                         !string.IsNullOrWhiteSpace(request.TrackId) &&
                         await _host.PlayNowAsync(request.TrackId);
                await WriteJsonAsync(context, new OkResponse(ok), RemoteJsonContext.Default.OkResponse);
                if (ok)
                {
                    NotifyStateChanged();
                }
            });

            app.MapPost("/api/queue/clear", async context =>
            {
                var clientId = await PrepareClientAsync(context, requireHost: true);
                if (clientId is null)
                {
                    return;
                }

                var ok = await _host.ClearQueueAsync();
                await WriteJsonAsync(context, new OkResponse(ok), RemoteJsonContext.Default.OkResponse);
                if (ok)
                {
                    NotifyStateChanged();
                }
            });

            app.MapGet("/api/playlists", async context =>
            {
                var clientId = await PrepareClientAsync(context, requireHost: true);
                if (clientId is null)
                {
                    return;
                }

                var playlists = await _host.GetPlaylistsAsync();
                await WriteJsonAsync(
                    context,
                    new List<RemotePlaylistItem>(playlists),
                    RemoteJsonContext.Default.ListRemotePlaylistItem);
            });

            app.MapPost("/api/playlists/add", async context =>
            {
                var clientId = await PrepareClientAsync(context, requireHost: true);
                if (clientId is null)
                {
                    return;
                }

                var request = await ReadJsonAsync(context, RemoteJsonContext.Default.PlaylistAddRequest);
                var ok = request is not null &&
                         !string.IsNullOrWhiteSpace(request.Id) &&
                         await _host.AppendPlaylistAsync(request.Id);
                await WriteJsonAsync(context, new OkResponse(ok), RemoteJsonContext.Default.OkResponse);
                if (ok)
                {
                    NotifyStateChanged();
                }
            });

            app.Map("/ws", async context =>
            {
                var clientId = await PrepareClientAsync(context, requireHost: false);
                if (clientId is null)
                {
                    return;
                }

                if (!context.WebSockets.IsWebSocketRequest)
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }

                using var socket = await context.WebSockets.AcceptWebSocketAsync();
                var client = new ClientSocket(socket, clientId);
                lock (_socketLock)
                {
                    _sockets[socket] = client;
                }

                _clients.SetConnected(clientId, true);
                try
                {
                    await SendStateAsync(client, await _host.GetSnapshotAsync());
                    var buffer = new byte[1024];
                    while (socket.State == WebSocketState.Open && !_cts.IsCancellationRequested)
                    {
                        var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            break;
                        }
                    }
                }
                catch
                {
                    // Disconnected phone.
                }
                finally
                {
                    lock (_socketLock)
                    {
                        _sockets.Remove(socket);
                    }

                    _clients.SetConnected(clientId, false);
                    client.SendLock.Dispose();
                }
            });

            await app.StartAsync();
            var server = app.Services.GetRequiredService<IServer>();
            var address = server.Features.Get<IServerAddressesFeature>()?.Addresses.FirstOrDefault();
            if (address is null)
            {
                await app.DisposeAsync();
                throw new InvalidOperationException("Не удалось определить адрес пульта.");
            }

            Port = new Uri(address).Port;
            _app = app;
            RefreshEndpoint();
            _broadcast = Task.Run(BroadcastLoopAsync);
        }
        catch
        {
            _app = null;
            throw;
        }
        finally
        {
            _startLock.Release();
        }
    }

    /// <summary>Пересчитывает адрес и QR после подключения к другой сети или хотспота.</summary>
    public void RefreshEndpoint(bool preferHotspot = false)
    {
        if (Port <= 0)
        {
            return;
        }

        var address = preferHotspot ? FindHotspotAddress() : FindLanAddress();
        Url = $"http://{address}:{Port}/?token={Token}";
        try
        {
            QrPng = CreateQrBytes(Url);
        }
        catch (Exception ex)
        {
            QrPng = Array.Empty<byte>();
            LogService.Warn($"Не удалось сгенерировать QR пульта: {ex.Message}");
        }

        try
        {
            QrPath = CreateQr(Url);
        }
        catch (Exception ex)
        {
            // QR может быть показан из памяти даже без доступного файла.
            QrPath = string.Empty;
            LogService.Warn($"Не удалось сохранить QR пульта: {ex.Message}");
        }
    }

    public void NotifyStateChanged()
    {
        if (_app is not null)
        {
            _ = BroadcastAsync();
        }
    }

    public void BanClient(string clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return;
        }

        _bannedClients[clientId] = true;
        _hostClients.TryRemove(clientId, out _);
        _adminClients.TryRemove(clientId, out _);
        _clients.SetConnected(clientId, false);
        NotifyStateChanged();
    }

    private async Task BroadcastLoopAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(_cts.Token))
            {
                await BroadcastAsync();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task BroadcastAsync()
    {
        try
        {
            var state = await _host.GetSnapshotAsync();
            ClientSocket[] clients;
            lock (_socketLock)
            {
                clients = _sockets.Values.ToArray();
            }

            // Отправляем параллельно: медленный телефон не должен задерживать
            // обновление состояния у остальных пультов.
            await Task.WhenAll(clients.Select(async client =>
            {
                try
                {
                    await SendStateAsync(client, state);
                }
                catch
                {
                    lock (_socketLock)
                    {
                        _sockets.Remove(client.Socket);
                    }

                    try
                    {
                        client.Socket.Abort();
                    }
                    catch
                    {
                        // Сокет мог быть уже закрыт.
                    }
                }
            }));
        }
        catch
        {
            // UI мог быть уже закрыт; следующий тик попробует снова.
        }
    }

    private async Task SendStateAsync(ClientSocket client, RemoteSnapshot state)
    {
        if (client.Socket.State != WebSocketState.Open)
        {
            return;
        }

        var payload = new StateResponse(
            state,
            _hostClients.ContainsKey(client.ClientId),
            _adminClients.ContainsKey(client.ClientId),
            client.ClientId,
            _clients.GetAliasName(client.ClientId) ?? string.Empty,
            _clients.GetFallbackName(client.ClientId) ?? string.Empty);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, RemoteJsonContext.Default.StateResponse);
        if (!await client.SendLock.WaitAsync(TimeSpan.FromSeconds(2), _cts.Token))
        {
            return;
        }

        try
        {
            // Полузакрытый сокет не должен блокировать общий цикл рассылки.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(4));
            await client.Socket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                true,
                timeout.Token);
        }
        finally
        {
            client.SendLock.Release();
        }
    }

    private async Task<string?> PrepareClientAsync(HttpContext context, bool requireHost)
    {
        if (!TryToken(context, out _))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return null;
        }

        var clientId = EnsureClient(context);
        await RegisterClientAsync(context, clientId);

        if (_bannedClients.ContainsKey(clientId))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return null;
        }

        if (requireHost && !_hostClients.ContainsKey(clientId))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return null;
        }

        return clientId;
    }

    private async Task RegisterClientAsync(HttpContext context, string clientId)
    {
        var address = context.Connection.RemoteIpAddress;
        if (!_fallbackNames.ContainsKey(clientId))
        {
            _fallbackNames[clientId] = await ResolveFallbackNameAsync(
                address,
                context.Request.Headers.UserAgent.ToString());
        }

        _clients.Register(
            clientId,
            address?.ToString() ?? string.Empty,
            MacAddressHelper.TryGet(address),
            _fallbackNames[clientId],
            _hostClients.ContainsKey(clientId));
    }

    private async Task<string> ResolveFallbackNameAsync(IPAddress? address, string userAgent)
    {
        if (address is null || IPAddress.IsLoopback(address))
        {
            return $"Гость {Interlocked.Increment(ref _guestCounter)}";
        }

        try
        {
            var lookup = Dns.GetHostEntryAsync(address);
            var completed = await Task.WhenAny(lookup, Task.Delay(500));
            if (completed == lookup)
            {
                var entry = await lookup;
                if (!string.IsNullOrWhiteSpace(entry.HostName) &&
                    !IPAddress.TryParse(entry.HostName, out _))
                {
                    return entry.HostName;
                }
            }
            else
            {
                _ = lookup.ContinueWith(
                    task => _ = task.Exception,
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted,
                    TaskScheduler.Default);
            }
        }
        catch
        {
            // DNS может быть недоступен — используем модель из User-Agent.
        }

        var device = DeviceNameFromUserAgent(userAgent);
        return string.IsNullOrWhiteSpace(device)
            ? $"Гость {Interlocked.Increment(ref _guestCounter)}"
            : device;
    }

    private static string? ResolveLocaleAsset(string relativePath)
    {
        var relative = relativePath.Replace('/', Path.DirectorySeparatorChar);
        var candidates = new[]
        {
            Path.Combine(LocalizationPaths.FindCatalogDirectory(), relative),
            Path.Combine(AppContext.BaseDirectory, "public", relative)
        };

        foreach (var candidate in candidates)
        {
            try
            {
                var full = Path.GetFullPath(candidate);
                if (File.Exists(full))
                {
                    return full;
                }
            }
            catch
            {
                // Ignore malformed paths and fall through to the next location.
            }
        }

        return null;
    }

    private static string? DeviceNameFromUserAgent(string userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return null;
        }

        if (userAgent.Contains("iPhone", StringComparison.OrdinalIgnoreCase))
        {
            return "iPhone";
        }

        if (userAgent.Contains("iPad", StringComparison.OrdinalIgnoreCase))
        {
            return "iPad";
        }

        var android = userAgent.IndexOf("Android", StringComparison.OrdinalIgnoreCase);
        if (android >= 0)
        {
            var semicolon = userAgent.IndexOf(';', android);
            if (semicolon >= 0)
            {
                var tail = userAgent[(semicolon + 1)..];
                var end = tail.IndexOf(" Build/", StringComparison.OrdinalIgnoreCase);
                if (end < 0)
                {
                    end = tail.IndexOf(')');
                }

                var model = end > 0 ? tail[..end].Trim() : tail.Trim();
                if (!string.IsNullOrWhiteSpace(model) &&
                    !model.Contains("wv", StringComparison.OrdinalIgnoreCase))
                {
                    return model;
                }
            }
        }

        return userAgent.Contains("Windows", StringComparison.OrdinalIgnoreCase)
            ? "Windows"
            : userAgent.Contains("Macintosh", StringComparison.OrdinalIgnoreCase) ? "Mac" : null;
    }

    private static async Task WriteJsonAsync<T>(
        HttpContext context,
        T value,
        JsonTypeInfo<T> typeInfo)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.Headers["Cache-Control"] = "no-store";
        await JsonSerializer.SerializeAsync(
            context.Response.Body,
            value,
            typeInfo,
            context.RequestAborted);
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpContext context, JsonTypeInfo<T> typeInfo)
    {
        try
        {
            return await JsonSerializer.DeserializeAsync(
                context.Request.Body,
                typeInfo,
                context.RequestAborted);
        }
        catch
        {
            return default;
        }
    }

    private bool TryToken(HttpContext context, out string token)
    {
        token = context.Request.Query["token"].ToString();
        if (string.Equals(token, Token, StringComparison.Ordinal))
        {
            LastClientActivityUtc = DateTime.UtcNow;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Идентификатор клиента выдаёт сервер через HttpOnly-cookie. Клиент больше
    /// не может подставить чужой id через query-строку.
    /// </summary>
    private static string EnsureClient(HttpContext context)
    {
        var existing = context.Request.Cookies[ClientCookie];
        if (!string.IsNullOrWhiteSpace(existing) && existing.Length >= 16)
        {
            return existing;
        }

        var clientId = Guid.NewGuid().ToString("N");
        context.Response.Cookies.Append(
            ClientCookie,
            clientId,
            new CookieOptions
            {
                HttpOnly = true,
                IsEssential = true,
                SameSite = SameSiteMode.Lax,
                Path = "/",
            });
        return clientId;
    }

    /// <summary>
    /// Checks an administrator PIN without counting an attempt. Used by
    /// /api/auth so a successful host sign-in does not slowly lock the admin
    /// role; the separated /api/admin endpoint counts its own attempts.
    /// </summary>
    private bool VerifyAdminPinQuiet(string pin)
    {
        lock (_adminPinAttempt)
        {
            if (DateTime.UtcNow < _adminPinAttempt.LockedUntilUtc)
            {
                return false;
            }

            if (_pins.Verify(PinRole.Admin, pin).Success)
            {
                _adminPinAttempt.Failures = 0;
                _adminPinAttempt.LockedUntilUtc = default;
                return true;
            }

            return false;
        }
    }

    private bool TryVerifyPin(string clientId, string pin)
    {
        if (_hostClients.ContainsKey(clientId))
        {
            return true;
        }

        // Счётчик общий на сервер: очистка cookie не сбрасывает лимит попыток.
        lock (_pinAttempt)
        {
            if (DateTime.UtcNow < _pinAttempt.LockedUntilUtc)
            {
                LogService.Warn("Host PIN rejected: lockout is active.", "security");
                return false;
            }

            if (_pins.Verify(PinRole.Host, pin).Success)
            {
                _pinAttempt.Failures = 0;
                _pinAttempt.LockedUntilUtc = default;
                return true;
            }

            _pinAttempt.Failures++;
            LogService.Warn(
                $"Host PIN rejected (attempt {_pinAttempt.Failures}/{MaxPinAttempts}).",
                "security");
            if (_pinAttempt.Failures >= MaxPinAttempts)
            {
                _pinAttempt.Failures = 0;
                _pinAttempt.LockedUntilUtc = DateTime.UtcNow.Add(PinLockout);
                LogService.Warn($"Host PIN locked for {PinLockout.TotalMinutes:0} minutes.", "security");
            }

            return false;
        }
    }

    private bool TryVerifyAdminPin(string clientId, string pin)
    {
        if (_adminClients.ContainsKey(clientId))
        {
            return true;
        }

        lock (_adminPinAttempt)
        {
            if (DateTime.UtcNow < _adminPinAttempt.LockedUntilUtc)
            {
                LogService.Warn("Administrator PIN rejected: lockout is active.", "security");
                return false;
            }

            if (_pins.Verify(PinRole.Admin, pin).Success)
            {
                _adminPinAttempt.Failures = 0;
                _adminPinAttempt.LockedUntilUtc = default;
                return true;
            }

            _adminPinAttempt.Failures++;
            LogService.Warn(
                $"Administrator PIN rejected (attempt {_adminPinAttempt.Failures}/{MaxPinAttempts}).",
                "security");
            if (_adminPinAttempt.Failures >= MaxPinAttempts)
            {
                _adminPinAttempt.Failures = 0;
                _adminPinAttempt.LockedUntilUtc = DateTime.UtcNow.Add(PinLockout);
                LogService.Warn($"Administrator PIN locked for {PinLockout.TotalMinutes:0} minutes.", "security");
            }

            return false;
        }
    }

    public static bool HasWifiNetwork()
    {
        try
        {
            foreach (var profile in NetworkInformation.GetConnectionProfiles())
            {
                if (profile.IsWlanConnectionProfile &&
                    profile.GetNetworkConnectivityLevel() != NetworkConnectivityLevel.None)
                {
                    return true;
                }
            }
        }
        catch
        {
            // If the profile API is unavailable, treat the network as absent.
        }

        return false;
    }

    public static string FindHotspotAddress()
    {
        foreach (var network in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (network.OperationalStatus != OperationalStatus.Up ||
                network.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            foreach (var address in network.GetIPProperties().UnicastAddresses)
            {
                if (address.Address.AddressFamily == AddressFamily.InterNetwork)
                {
                    var text = address.Address.ToString();
                    if (network.Description.Contains("Wi-Fi Direct", StringComparison.OrdinalIgnoreCase) ||
                        text.StartsWith("192.168.137.", StringComparison.Ordinal))
                    {
                        return text;
                    }
                }
            }
        }

        return "127.0.0.1";
    }

    public static bool HasInternetAccess()
    {
        try
        {
            foreach (var profile in NetworkInformation.GetConnectionProfiles())
            {
                if (!profile.IsWlanConnectionProfile)
                {
                    continue;
                }

                if (profile.GetNetworkConnectivityLevel() is
                    NetworkConnectivityLevel.InternetAccess or
                    NetworkConnectivityLevel.ConstrainedInternetAccess)
                {
                    return true;
                }
            }
        }
        catch
        {
            // Fall through to "no internet".
        }

        return false;
    }

    public static string FindLanAddress()
    {
        var candidates = new List<(int Score, string Address, NetworkInterfaceType Type)>();
        foreach (var network in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (network.OperationalStatus != OperationalStatus.Up ||
                network.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            var typeScore = network.NetworkInterfaceType switch
            {
                NetworkInterfaceType.Wireless80211 => 4,
                NetworkInterfaceType.Ethernet => 3,
                _ => 1,
            };

            var description = network.Description ?? string.Empty;
            var isVirtual = description.Contains("VMware", StringComparison.OrdinalIgnoreCase) ||
                description.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase) ||
                description.Contains("WSL", StringComparison.OrdinalIgnoreCase) ||
                description.Contains("Virtual", StringComparison.OrdinalIgnoreCase) ||
                description.Contains("TAP-", StringComparison.OrdinalIgnoreCase) ||
                description.Contains("TUN", StringComparison.OrdinalIgnoreCase);
            var hasGateway = false;
            try
            {
                hasGateway = network.GetIPProperties().GatewayAddresses.Any(gateway =>
                    gateway.Address.AddressFamily == AddressFamily.InterNetwork &&
                    !gateway.Address.Equals(IPAddress.Any));
            }
            catch
            {
                // A disconnected adapter can throw while enumerating gateways.
            }

            foreach (var address in network.GetIPProperties().UnicastAddresses)
            {
                if (address.Address.AddressFamily != AddressFamily.InterNetwork ||
                    IPAddress.IsLoopback(address.Address))
                {
                    continue;
                }

                var text = address.Address.ToString();
                if (text.StartsWith("169.254.", StringComparison.Ordinal) ||
                    text.StartsWith("198.18.", StringComparison.Ordinal) ||
                    text.StartsWith("198.19.", StringComparison.Ordinal) ||
                    text.StartsWith("192.168.137.", StringComparison.Ordinal))
                {
                    continue;
                }

                var score = typeScore;
                if (hasGateway)
                {
                    score += 20;
                }

                if (isVirtual)
                {
                    score -= 15;
                }

                if (text.StartsWith("192.168.", StringComparison.Ordinal))
                {
                    score += 10;
                }
                else if (text.StartsWith("10.", StringComparison.Ordinal))
                {
                    score += 8;
                }
                else if (IsPrivate172(text))
                {
                    score += 6;
                }
                else
                {
                    score -= 10;
                }

                candidates.Add((score, text, network.NetworkInterfaceType));
            }
        }

        var best = candidates
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Type == NetworkInterfaceType.Wireless80211 ? 0 : 1)
            .FirstOrDefault();

        return string.IsNullOrEmpty(best.Address) ? "127.0.0.1" : best.Address;
    }

    private static bool IsPrivate172(string address)
    {
        var parts = address.Split('.');
        return parts.Length == 4 &&
               parts[0] == "172" &&
               int.TryParse(parts[1], out var second) &&
               second is >= 16 and <= 31;
    }

    private static string CreateQr(string value) => CreateQrFile(value, "remote-qr");

    public static byte[] CreateWifiQrBytes(string ssid, string password)
    {
        static string Escape(string value) => value
            .Replace("\\", "\\\\")
            .Replace(";", "\\;")
            .Replace(",", "\\,")
            .Replace(":", "\\:")
            .Replace("\"", "\\\"");

        return CreateQrBytes($"WIFI:T:WPA;S:{Escape(ssid)};P:{Escape(password)};;");
    }

    /// <summary>QR-строка формата WIFI: для подключения телефона к точке доступа.</summary>
    public static string CreateWifiQr(string ssid, string password)
    {
        var bytes = CreateWifiQrBytes(ssid, password);
        var folder = Path.Combine(AppStorage.Folder, "Remote");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"wifi-qr-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static byte[] CreateQrBytes(string value)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(value, QRCodeGenerator.ECCLevel.Q);
        var code = new PngByteQRCode(data);
        return code.GetGraphic(8);
    }

    private static string CreateQrFile(string value, string prefix)
    {
        var bytes = CreateQrBytes(value);
        var folder = Path.Combine(AppStorage.Folder, "Remote");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"{prefix}-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(path, bytes);

        // Старые QR-файлы не нужны: иначе папка данных растёт с каждым обновлением адреса.
        try
        {
            foreach (var old in Directory.EnumerateFiles(folder, $"{prefix}*.png"))
            {
                if (!string.Equals(old, path, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(old);
                }
            }
        }
        catch
        {
            // Удаление старого QR не должно ломать запуск пульта.
        }

        return path;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cts.Cancel();

        lock (_socketLock)
        {
            foreach (var client in _sockets.Values)
            {
                try
                {
                    client.Socket.Dispose();
                }
                catch
                {
                }
            }

            _sockets.Clear();
        }

        _clients.DisconnectAll();

        if (_broadcast is not null)
        {
            try
            {
                await _broadcast;
            }
            catch
            {
            }
        }

        if (_app is not null)
        {
            try
            {
                using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await _app.StopAsync(stopCts.Token);
            }
            catch
            {
            }

            await _app.DisposeAsync();
            _app = null;
        }

        _cts.Dispose();
        _startLock.Dispose();
    }

    private sealed class ClientSocket
    {
        public ClientSocket(WebSocket socket, string clientId)
        {
            Socket = socket;
            ClientId = clientId;
        }

        public WebSocket Socket { get; }

        public string ClientId { get; }

        public SemaphoreSlim SendLock { get; } = new(1, 1);
    }

    private sealed class PinAttempt
    {
        public int Failures { get; set; }

        public DateTime LockedUntilUtc { get; set; }
    }
}
