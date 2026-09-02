namespace WinTilix.Updates;

/// <summary>
/// Caches update checks for a short window so a long-running instance keeps
/// noticing new releases instead of reusing the answer it got at startup.
/// </summary>
public sealed class UpdateCheckCoordinator
{
    private readonly Func<CancellationToken, Task<UpdateCheckResult>> _check;
    private readonly Func<DateTimeOffset> _clock;
    private readonly TimeSpan _freshness;
    private readonly object _gate = new();
    private Task<UpdateCheckResult>? _inFlight;
    private UpdateCheckResult? _cached;
    private DateTimeOffset _cachedAt;

    public UpdateCheckCoordinator(
        Func<CancellationToken, Task<UpdateCheckResult>> check,
        TimeSpan? freshness = null,
        Func<DateTimeOffset>? clock = null)
    {
        ArgumentNullException.ThrowIfNull(check);

        _check = check;
        _freshness = freshness ?? TimeSpan.FromMinutes(15);
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public UpdateCheckResult? LastResult
    {
        get
        {
            lock (_gate)
            {
                return _cached;
            }
        }
    }

    public DateTimeOffset? LastCheckedAt
    {
        get
        {
            lock (_gate)
            {
                return _cached is null ? null : _cachedAt;
            }
        }
    }

    public Task<UpdateCheckResult> GetAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!forceRefresh && _cached is not null && _clock() - _cachedAt < _freshness)
            {
                return Task.FromResult(_cached);
            }

            if (_inFlight is not null)
            {
                return _inFlight;
            }

            var task = RunAsync(cancellationToken);
            if (task.IsCompleted)
            {
                // Nothing to share: a finished task must never be handed out again.
                return task;
            }

            _inFlight = task;
            _ = task.ContinueWith(ClearInFlight, TaskScheduler.Default);
            return task;
        }
    }

    private void ClearInFlight(Task<UpdateCheckResult> task)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_inFlight, task))
            {
                _inFlight = null;
            }
        }
    }

    private async Task<UpdateCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _check(cancellationToken).ConfigureAwait(false);
            lock (_gate)
            {
                _cached = result;
                _cachedAt = _clock();
            }

            return result;
        }
        catch (Exception)
        {
            // A failed check must not be remembered, so the next attempt retries.
            lock (_gate)
            {
                _cached = null;
            }

            throw;
        }
    }
}
