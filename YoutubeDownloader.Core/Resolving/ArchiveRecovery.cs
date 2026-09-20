using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using YoutubeDownloader.Core.Utils;
using YoutubeExplode.Videos;

namespace YoutubeDownloader.Core.Resolving;

/// <summary>
/// Attempts to recover a terminated video's media from an Internet Archive capture.
/// </summary>
public static class ArchiveRecovery
{
    public static async Task<bool> TryDownloadAsync(
        VideoId videoId,
        string filePath,
        CancellationToken cancellationToken = default
    )
    {
        var watchUrl = $"https://www.youtube.com/watch?v={videoId}";
        var cdxUrl =
            $"https://web.archive.org/cdx/search/cdx?url={Uri.EscapeDataString(watchUrl)}"
            + "&output=json&filter=statuscode:200&fl=timestamp,original&collapse=digest";

        using var cdxResponse = await Http.Client.GetAsync(cdxUrl, cancellationToken);
        if (!cdxResponse.IsSuccessStatusCode)
            return false;

        await using var cdxStream = await cdxResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var cdxDocument = await JsonDocument.ParseAsync(cdxStream, cancellationToken: cancellationToken);
        if (cdxDocument.RootElement.ValueKind != JsonValueKind.Array)
            return false;

        foreach (var capture in cdxDocument.RootElement.EnumerateArray())
        {
            if (capture.ValueKind != JsonValueKind.Array || capture.GetArrayLength() < 2)
                continue;

            var timestamp = capture[0].GetString();
            var originalUrl = capture[1].GetString();
            if (string.IsNullOrWhiteSpace(timestamp) || string.IsNullOrWhiteSpace(originalUrl))
                continue;

            var replayUrl = $"https://web.archive.org/web/{timestamp}id_/{originalUrl}";
            using var mediaResponse = await Http.Client.GetAsync(
                replayUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken
            );

            if (!mediaResponse.IsSuccessStatusCode)
                continue;

            var mediaType = mediaResponse.Content.Headers.ContentType?.MediaType;
            if (mediaType is not null && !mediaType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
                continue;

            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var temporaryPath = filePath + ".download";
            try
            {
                await using (var input = await mediaResponse.Content.ReadAsStreamAsync(cancellationToken))
                await using (var output = File.Create(temporaryPath))
                    await input.CopyToAsync(output, cancellationToken);

                File.Move(temporaryPath, filePath, true);
                return true;
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }

        return false;
    }
}
