using FinalCheck.Core.Abstractions;

namespace FinalCheck.Infrastructure;

public sealed class DeferredUpdateService : IUpdateService
{
    public ValueTask<UpdateCheckResult> CheckForUpdatesAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new UpdateCheckResult(false, null, null));
    }
}
