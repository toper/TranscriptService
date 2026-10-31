using Microsoft.Extensions.Options;
using TranscriptService.Api.Options;

namespace TranscriptService.Api.Jobs;

/// <summary>Recordings on the volume shared with the engine workers, named after their job.</summary>
public sealed partial class AudioStorage(IOptions<ApiOptions> options, ILogger<AudioStorage>? logger = null)
{
    private readonly string _root = options.Value.StoragePath;

    public string PathFor(Guid jobId) => Path.Combine(_root, $"{jobId}.wav");

    public async Task<string> SaveAsync(Guid jobId, Stream audio, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(audio);

        Directory.CreateDirectory(_root);
        var path = PathFor(jobId);
        var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 1 << 16, FileOptions.Asynchronous);
        await using (file.ConfigureAwait(false))
        {
            await audio.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
        }

        return path;
    }

    /// <summary>Full paths of all stored recordings.</summary>
    public IEnumerable<string> EnumerateRecordings() =>
        Directory.Exists(_root) ? Directory.EnumerateFiles(_root, "*.wav") : [];

    /// <summary>Deletes a recording; a file that is already gone is not an error.</summary>
    public void Delete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException ex)
        {
            if (logger is not null)
            {
                LogDeleteFailed(logger, ex, path);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not delete recording {Path}")]
    private static partial void LogDeleteFailed(ILogger logger, Exception exception, string path);
}
