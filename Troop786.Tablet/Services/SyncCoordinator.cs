using Troop786.Core;

namespace Troop786.Tablet.Services;

/// <summary>
/// Flushes the vote queue when connectivity returns and on a timer, and tells the UI how many votes are waiting.
/// </summary>
public sealed class SyncCoordinator : IDisposable
{
    private readonly OutboxSyncService _sync;
    private readonly IVoteOutbox _outbox;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Timer _timer;

    public event Action<int, bool>? StatusChanged; // pending votes, online

    public SyncCoordinator(OutboxSyncService sync, IVoteOutbox outbox)
    {
        _sync = sync;
        _outbox = outbox;
        Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;
        _timer = new Timer(_ => _ = FlushAsync(), null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30));
    }

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        if (e.NetworkAccess == NetworkAccess.Internet)
            _ = FlushAsync();
        else
            _ = PublishAsync(false);
    }

    public async Task FlushAsync()
    {
        // One flush at a time; a second trigger while one runs is skipped, the timer catches up.
        if (!await _gate.WaitAsync(0)) return;
        try
        {
            var summary = await _sync.FlushAsync();
            await PublishAsync(!summary.WentOffline);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task PublishAsync(bool online)
    {
        var pending = await _outbox.PendingCountAsync(CancellationToken.None);
        StatusChanged?.Invoke(pending, online);
    }

    public void Dispose()
    {
        Connectivity.Current.ConnectivityChanged -= OnConnectivityChanged;
        _timer.Dispose();
        _gate.Dispose();
    }
}
