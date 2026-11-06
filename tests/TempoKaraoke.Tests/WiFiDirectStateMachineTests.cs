using TempoKaraoke.Networking;
using Xunit;

namespace TempoKaraoke.Tests;

public sealed class WiFiDirectStateMachineTests
{
    [Fact]
    public void HappyPathWalksThroughTheAdvertisedStates()
    {
        var machine = new WiFiDirectStateMachine();

        Assert.True(machine.TryTransition(WiFiDirectState.CheckingSupport));
        Assert.True(machine.TryTransition(WiFiDirectState.Starting));
        Assert.True(machine.TryTransition(WiFiDirectState.Advertising));
        Assert.True(machine.TryTransition(WiFiDirectState.WaitingForConnection));
        Assert.True(machine.TryTransition(WiFiDirectState.Connected));
        Assert.True(machine.TryTransition(WiFiDirectState.Stopping));
        Assert.True(machine.TryTransition(WiFiDirectState.Idle));
        Assert.Equal(WiFiDirectState.Idle, machine.State);
    }

    [Fact]
    public void InvalidTransitionsAreRejected()
    {
        var machine = new WiFiDirectStateMachine();

        Assert.False(machine.TryTransition(WiFiDirectState.Advertising));
        Assert.False(machine.TryTransition(WiFiDirectState.Connected));
        Assert.Equal(WiFiDirectState.Idle, machine.State);
    }

    [Fact]
    public void ErrorKeepsTheReasonAndAllowsRetry()
    {
        var machine = new WiFiDirectStateMachine();
        machine.TryTransition(WiFiDirectState.CheckingSupport);
        machine.Fail("adapter is off");

        Assert.Equal(WiFiDirectState.Error, machine.State);
        Assert.Equal("adapter is off", machine.LastError);
        Assert.True(machine.TryTransition(WiFiDirectState.CheckingSupport));
        Assert.True(machine.TryTransition(WiFiDirectState.Starting));
        Assert.Equal(string.Empty, machine.LastError);
    }

    [Fact]
    public void ActiveStateReportsAreConsistent()
    {
        var machine = new WiFiDirectStateMachine();
        Assert.False(machine.IsActive);
        Assert.True(machine.CanStart);

        machine.TryTransition(WiFiDirectState.CheckingSupport);
        machine.TryTransition(WiFiDirectState.Starting);
        Assert.True(machine.IsActive);
        Assert.False(machine.CanStart);
    }
}
