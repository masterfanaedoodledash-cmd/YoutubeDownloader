using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using YoutubeDownloader.Core.Utils;
using YoutubeExplode.Videos;

namespace YoutubeDownloader.Core.Resolving;

/// <summary>
/// Attempts to recover a terminated video's media from known archive locations.
/// </summary>
public static class ArchiveRecovery
{
    public static async Task<bool> TryDownloadAsync(
        VideoId videoId,
        string filePath,
        CancellationToken cancellationToken = default
    )
    {
        var id = videoId.ToString();
        var sources = new[]
        {
            $"https://archive.org/download/{id}/{id}.mp4",
            $"https://archive.org/download/youtube-{id}/{id}.mp4",
            $"https://web.archive.org/web/2if_/{Uri.EscapeDataString($"https://www.youtube.com/watch?v={id}")}",
        };

        foreach (var source in sources)
        {
            if (await TryDownloadSourceAsync(source, filePath, cancellationToken))
                return true;
        }

        return false;
    }

    private static async Task<bool> TryDownloadSourceAsync(
        string source,
        string filePath,
        CancellationToken cancellationToken
    )
    {
        using var response = await Http.Client.GetAsync(
            source,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );

        if (!response.IsSuccessStatusCode)
            return false;

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType is not null && !mediaType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
            return false;

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var temporaryPath = filePath + ".download";
        try
        {
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = File.Create(temporaryPath))
                await input.CopyToAsync(output, cancellationToken);

            if (new FileInfo(temporaryPath).Length == 0)
                return false;

            File.Move(temporaryPath, filePath, true);
            return true;
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}
