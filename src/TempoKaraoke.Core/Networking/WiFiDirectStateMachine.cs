namespace TempoKaraoke.Networking;

/// <summary>Explicit Wi-Fi Direct lifecycle shown by the UI instead of ad-hoc flags.</summary>
public enum WiFiDirectState
{
    Idle,
    CheckingSupport,
    Starting,
    Advertising,
    WaitingForConnection,
    Connected,
    Stopping,
    Error,
}

/// <summary>
/// UI-independent state machine for Wi-Fi Direct operations. Every transition
/// is validated so a stale async completion cannot move the UI backwards.
/// </summary>
public sealed class WiFiDirectStateMachine
{
    private readonly object _sync = new();

    public WiFiDirectState State { get; private set; } = WiFiDirectState.Idle;

    public string LastError { get; private set; } = string.Empty;

    public bool IsActive =>
        State is WiFiDirectState.Starting
            or WiFiDirectState.Advertising
            or WiFiDirectState.WaitingForConnection
            or WiFiDirectState.Connected;

    public bool CanStart => State is WiFiDirectState.Idle or WiFiDirectState.Error;

    public bool TryTransition(WiFiDirectState next)
    {
        lock (_sync)
        {
            if (!IsAllowed(State, next))
            {
                return false;
            }

            State = next;
            if (next is not WiFiDirectState.Error)
            {
                LastError = string.Empty;
            }

            return true;
        }
    }

    /// <summary>Moves to Error and keeps a human-readable reason for diagnostics.</summary>
    public void Fail(string message)
    {
        lock (_sync)
        {
            State = WiFiDirectState.Error;
            LastError = message ?? string.Empty;
        }
    }

    public void Reset()
    {
        lock (_sync)
        {
            State = WiFiDirectState.Idle;
            LastError = string.Empty;
        }
    }

    public static bool IsAllowed(WiFiDirectState from, WiFiDirectState to)
    {
        if (from == to)
        {
            return true;
        }

        return from switch
        {
            WiFiDirectState.Idle => to is WiFiDirectState.CheckingSupport,
            WiFiDirectState.CheckingSupport => to is WiFiDirectState.Starting or WiFiDirectState.Error or WiFiDirectState.Idle,
            WiFiDirectState.Starting => to is WiFiDirectState.Advertising or WiFiDirectState.Stopping or WiFiDirectState.Error,
            WiFiDirectState.Advertising => to is WiFiDirectState.WaitingForConnection or WiFiDirectState.Connected or WiFiDirectState.Stopping or WiFiDirectState.Error,
            WiFiDirectState.WaitingForConnection => to is WiFiDirectState.Advertising or WiFiDirectState.Connected or WiFiDirectState.Stopping or WiFiDirectState.Error,
            WiFiDirectState.Connected => to is WiFiDirectState.WaitingForConnection or WiFiDirectState.Stopping or WiFiDirectState.Error,
            WiFiDirectState.Stopping => to is WiFiDirectState.Idle or WiFiDirectState.Error,
            WiFiDirectState.Error => to is WiFiDirectState.CheckingSupport or WiFiDirectState.Idle,
            _ => false,
        };
    }
}
