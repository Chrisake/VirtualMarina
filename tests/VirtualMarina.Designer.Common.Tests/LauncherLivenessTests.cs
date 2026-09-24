namespace VirtualMarina.Designer.Common.Tests;

/// <summary>When the desktop launcher decides its window is gone.</summary>
public class LauncherLivenessTests
{
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan FirstConnection = TimeSpan.FromSeconds(60);

    private static LauncherLiveness Watch(ManualClock clock) => new(clock, Grace, FirstConnection);

    [Fact]
    public void Waits_for_the_first_page_and_gives_up_after_a_minute()
    {
        var clock = new ManualClock();
        var liveness = Watch(clock);

        clock.Advance(TimeSpan.FromSeconds(59));
        Assert.Equal(LivenessVerdict.Alive, liveness.Check());
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(LivenessVerdict.NeverConnected, liveness.Check());
        Assert.False(liveness.EverConnected);
    }

    [Fact]
    public void Stays_up_while_a_page_is_connected_however_long()
    {
        var clock = new ManualClock();
        var liveness = Watch(clock);
        liveness.Connected("page-1");

        clock.Advance(TimeSpan.FromHours(5));

        Assert.Equal(LivenessVerdict.Alive, liveness.Check());
        Assert.Equal(1, liveness.OpenConnections);
    }

    [Fact]
    public void Stops_a_grace_period_after_the_last_page_went_away()
    {
        var clock = new ManualClock();
        var liveness = Watch(clock);
        liveness.Connected("page-1");
        clock.Advance(TimeSpan.FromMinutes(10));

        liveness.Disconnected("page-1");
        clock.Advance(Grace - TimeSpan.FromMilliseconds(1));
        Assert.Equal(LivenessVerdict.Alive, liveness.Check());
        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(LivenessVerdict.AllClientsGone, liveness.Check());
    }

    [Fact]
    public void A_reload_within_the_grace_period_keeps_it_up()
    {
        var clock = new ManualClock();
        var liveness = Watch(clock);
        liveness.Connected("before-reload");

        liveness.Leave("before-reload");
        clock.Advance(TimeSpan.FromSeconds(3));
        liveness.Connected("after-reload");
        liveness.Disconnected("before-reload");   // the old page's connection is only noticed gone now
        clock.Advance(TimeSpan.FromMinutes(1));

        Assert.Equal(LivenessVerdict.Alive, liveness.Check());
        Assert.Equal(1, liveness.OpenConnections);
    }

    [Fact]
    public void A_page_reconnecting_before_its_old_connection_closed_is_counted_once_gone()
    {
        var clock = new ManualClock();
        var liveness = Watch(clock);
        liveness.Connected("page");
        liveness.Connected("page");

        liveness.Disconnected("page");
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(LivenessVerdict.Alive, liveness.Check());

        liveness.Disconnected("page");
        clock.Advance(Grace);
        Assert.Equal(LivenessVerdict.AllClientsGone, liveness.Check());
    }

    [Fact]
    public void Saying_goodbye_ends_a_page_whatever_it_still_holds_open()
    {
        var clock = new ManualClock();
        var liveness = Watch(clock);
        liveness.Connected("page");
        liveness.Connected("page");

        liveness.Leave("page");
        liveness.Leave("page");
        liveness.Disconnected("unknown");
        clock.Advance(Grace);

        Assert.Equal(LivenessVerdict.AllClientsGone, liveness.Check());
        Assert.Equal(0, liveness.OpenConnections);
    }

    [Fact]
    public void Blank_page_ids_are_refused()
    {
        var liveness = new LauncherLiveness();

        Assert.Throws<ArgumentException>(() => liveness.Connected(string.Empty));
        Assert.Throws<ArgumentNullException>(() => liveness.Leave(null!));
        Assert.Equal(LauncherLiveness.DefaultGracePeriod, liveness.GracePeriod);
        Assert.Equal(LauncherLiveness.DefaultFirstConnectionTimeout, liveness.FirstConnectionTimeout);
    }

    [Fact]
    public async Task The_watch_loop_ends_with_the_verdict()
    {
        var liveness = new LauncherLiveness(TimeProvider.System, TimeSpan.FromMilliseconds(50), TimeSpan.FromSeconds(30));
        liveness.Connected("page");
        var watch = liveness.WaitUntilGoneAsync(TimeSpan.FromMilliseconds(10), CancellationToken.None);

        await Task.Delay(50);
        Assert.False(watch.IsCompleted);
        liveness.Disconnected("page");

        Assert.Equal(LivenessVerdict.AllClientsGone, await watch.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task The_watch_loop_can_be_cancelled()
    {
        var liveness = new LauncherLiveness();
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => liveness.WaitUntilGoneAsync(TimeSpan.FromMilliseconds(5), stop.Token));
    }
}
