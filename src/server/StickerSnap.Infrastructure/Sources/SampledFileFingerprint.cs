using System.Security.Cryptography;
using StickerSnap.Application.Sources;
using StickerSnap.Core.Errors;

namespace StickerSnap.Infrastructure.Sources;

/// <summary>
/// `v1:&lt;size&gt;:&lt;sha256&gt;`: the size and a hash of the first and the last mebibyte. Hashing a whole
/// video of a gigabyte would hold «Читаю файл» for seconds; another video of the same size with the
/// same start and end is not a case worth that.
/// </summary>
internal sealed class SampledFileFingerprint : IFileFingerprint
{
    internal const int SampleBytes = 1 << 20;

    public async Task<string> OfAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 1, FileOptions.Asynchronous);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[SampleBytes];

            var head = await file.ReadAtLeastAsync(buffer, SampleBytes, throwOnEndOfStream: false, cancellationToken);
            hash.AppendData(buffer, 0, head);
            if (file.Length > SampleBytes)
            {
                // The tail never overlaps the head: a file under 2 MiB gives its tail from where the head ended
                file.Seek(Math.Max(SampleBytes, file.Length - SampleBytes), SeekOrigin.Begin);
                var tail = await file.ReadAtLeastAsync(buffer, SampleBytes, throwOnEndOfStream: false, cancellationToken);
                hash.AppendData(buffer, 0, tail);
            }

            return $"v1:{file.Length}:{Convert.ToHexStringLower(hash.GetHashAndReset())}";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new AppException(ErrorCode.SourceNotFound, $"Cannot read {path}: {exception.Message}", exception);
        }
    }
}
