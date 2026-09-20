using System;
using System.Collections.Generic;
using System.Net;
using YoutubeDownloader.Core.Utils;
using YoutubeExplode.Common;
using YoutubeExplode.Videos;

namespace YoutubeDownloader.Core.Resolving;

/// <summary>
/// Video metadata recovered from an archived YouTube watch page.
/// </summary>
internal sealed class RecoveredVideo : IVideo
{
    public RecoveredVideo(
        VideoId id,
        string title,
        string authorName,
        string? authorChannelId,
        string? thumbnailUrl
    )
    {
        Id = id;
        Title = string.IsNullOrWhiteSpace(title) ? $"Recovered video ({id})" : title;

        var channelId = YoutubeDownloader.Core.Resolving.RecoveryResolver.GetFallbackChannelId(
            authorChannelId
        );
        Author = new Author(channelId, string.IsNullOrWhiteSpace(authorName) ? "Unknown channel" : authorName);

        Thumbnails =
        [
            new Thumbnail(
                thumbnailUrl ?? $"https://i.ytimg.com/vi/{id}/hqdefault.jpg",
                new Resolution(480, 360)
            ),
        ];
    }

    public VideoId Id { get; }

    public string Url => $"https://www.youtube.com/watch?v={Id}";

    public string Title { get; }

    public Author Author { get; }

    public TimeSpan? Duration => null;

    public IReadOnlyList<Thumbnail> Thumbnails { get; }
}
