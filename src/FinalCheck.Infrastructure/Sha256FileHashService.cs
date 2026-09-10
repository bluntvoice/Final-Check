using System.Security.Cryptography;
using FinalCheck.Core.Abstractions;

namespace FinalCheck.Infrastructure;

public sealed class Sha256FileHashService : IFileHashService
{
    public async ValueTask<string> ComputeSha256Async(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var originalPosition = stream.CanSeek ? stream.Position : (long?)null;
        try
        {
            if (stream.CanSeek)
            {
                stream.Position = 0;
            }

            var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
        finally
        {
            if (originalPosition is not null)
            {
                stream.Position = originalPosition.Value;
            }
        }
    }
}
