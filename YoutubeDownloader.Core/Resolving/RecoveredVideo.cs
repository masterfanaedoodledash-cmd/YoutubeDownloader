using System;
using System.Collections.Generic;
using YoutubeExplode.Common;
using YoutubeExplode.Videos;

namespace YoutubeDownloader.Core.Resolving;

/// <summary>
/// Video metadata recovered from an external archive.
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
        Author = new Author(
            new Channels.ChannelId(authorChannelId ?? ""),
            string.IsNullOrWhiteSpace(authorName) ? "Unknown channel" : authorName
        );
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
