namespace VirtualMarina.Designer;

/// <summary>What <see cref="LauncherLiveness.Check"/> concluded.</summary>
public enum LivenessVerdict
{
    /// <summary>A page is connected, or may still connect: keep serving.</summary>
    Alive = 0,

    /// <summary>No page ever connected within the time allowed: the window never opened.</summary>
    NeverConnected = 1,

    /// <summary>Every page went away and none came back within the grace period: the window was closed.</summary>
    AllClientsGone = 2,
}

/// <summary>
/// Decides when the desktop launcher should stop serving: once the last designer page has gone and not come back.
/// </summary>
/// <remarks>
/// <para>
/// The launcher cannot always tell when its window closes by watching a process: Chrome installed as a Flatpak or a
/// snap hands the window to a sandboxed instance, an already running browser takes the window into its own process,
/// and the default browser is opened through the desktop, which returns at once. So the page itself says it is there.
/// Each designer page served by the launcher holds a connection open to it; when the last one drops, and none comes
/// back within <see cref="GracePeriod"/> (long enough for a reload), the window is taken to be closed.
/// </para>
/// <para>
/// A page is known by an id it makes up when it loads. It may briefly hold two connections under that id (the browser
/// reconnecting before the old connection is noticed gone), so connections are counted per page, and a page that says
/// goodbye (<see cref="Leave"/>) is forgotten at once, whatever it still holds.
/// </para>
/// <para>Thread-safe: the server calls it from its request threads, the launcher's watch loop from its own.</para>
/// </remarks>
public sealed class LauncherLiveness
{
    /// <summary>How long after the last page went away the launcher waits for one to come back: enough for a reload.</summary>
    public static readonly TimeSpan DefaultGracePeriod = TimeSpan.FromSeconds(8);

    /// <summary>How long the launcher waits for the first page before deciding the window never opened.</summary>
    public static readonly TimeSpan DefaultFirstConnectionTimeout = TimeSpan.FromSeconds(60);

    private readonly object _lock = new();
    private readonly TimeProvider _time;
    private readonly Dictionary<string, int> _connections = new(StringComparer.Ordinal);
    private readonly DateTimeOffset _started;
    private DateTimeOffset _emptySince;
    private bool _everConnected;

    /// <summary>Starts watching, as of now.</summary>
    /// <param name="time">The clock; <see cref="TimeProvider.System"/> when null.</param>
    /// <param name="gracePeriod">How long to wait for a page to come back; <see cref="DefaultGracePeriod"/> when null.</param>
    /// <param name="firstConnectionTimeout">How long to wait for the first page; <see cref="DefaultFirstConnectionTimeout"/> when null.</param>
    public LauncherLiveness(TimeProvider? time = null, TimeSpan? gracePeriod = null, TimeSpan? firstConnectionTimeout = null)
    {
        _time = time ?? TimeProvider.System;
        GracePeriod = gracePeriod ?? DefaultGracePeriod;
        FirstConnectionTimeout = firstConnectionTimeout ?? DefaultFirstConnectionTimeout;
        _started = _time.GetUtcNow();
        _emptySince = _started;
    }

    /// <summary>How long after the last page went away the launcher waits for one to come back.</summary>
    public TimeSpan GracePeriod { get; }

    /// <summary>How long the launcher waits for the first page.</summary>
    public TimeSpan FirstConnectionTimeout { get; }

    /// <summary>How many connections are open, over all pages.</summary>
    public int OpenConnections
    {
        get
        {
            lock (_lock) return _connections.Values.Sum();
        }
    }

    /// <summary>True once any page has connected.</summary>
    public bool EverConnected
    {
        get
        {
            lock (_lock) return _everConnected;
        }
    }

    /// <summary>A page opened a connection.</summary>
    /// <param name="clientId">The id the page made up when it loaded.</param>
    public void Connected(string clientId)
    {
        ArgumentException.ThrowIfNullOrEmpty(clientId);
        lock (_lock)
        {
            _connections[clientId] = _connections.GetValueOrDefault(clientId) + 1;
            _everConnected = true;
        }
    }

    /// <summary>One of a page's connections closed. Unknown ids are ignored.</summary>
    /// <param name="clientId">The page's id.</param>
    public void Disconnected(string clientId)
    {
        ArgumentException.ThrowIfNullOrEmpty(clientId);
        lock (_lock)
        {
            if (!_connections.TryGetValue(clientId, out var count)) return;
            if (count > 1) _connections[clientId] = count - 1;
            else Forget(clientId);
        }
    }

    /// <summary>A page said it is going away (it is being closed or reloaded): it no longer counts, whatever it still holds open.</summary>
    /// <param name="clientId">The page's id.</param>
    public void Leave(string clientId)
    {
        ArgumentException.ThrowIfNullOrEmpty(clientId);
        lock (_lock)
        {
            if (_connections.ContainsKey(clientId)) Forget(clientId);
        }
    }

    /// <summary>Whether to keep serving, as of now.</summary>
    public LivenessVerdict Check()
    {
        var now = _time.GetUtcNow();
        lock (_lock)
        {
            if (_connections.Count > 0) return LivenessVerdict.Alive;
            if (!_everConnected) return now - _started >= FirstConnectionTimeout ? LivenessVerdict.NeverConnected : LivenessVerdict.Alive;
            return now - _emptySince >= GracePeriod ? LivenessVerdict.AllClientsGone : LivenessVerdict.Alive;
        }
    }

    /// <summary>Checks every <paramref name="interval"/> until the verdict is no longer <see cref="LivenessVerdict.Alive"/>.</summary>
    /// <param name="interval">How often to check.</param>
    /// <param name="cancellationToken">Stops the watch early; the task is then cancelled.</param>
    public async Task<LivenessVerdict> WaitUntilGoneAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        while (true)
        {
            var verdict = Check();
            if (verdict != LivenessVerdict.Alive) return verdict;
            await Task.Delay(interval, _time, cancellationToken).ConfigureAwait(false);
        }
    }

    private void Forget(string clientId)
    {
        _connections.Remove(clientId);
        if (_connections.Count == 0) _emptySince = _time.GetUtcNow();
    }
}
