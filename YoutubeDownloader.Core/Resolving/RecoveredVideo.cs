using System;
using System.Collections.Generic;
using YoutubeExplode.Channels;
using YoutubeExplode.Common;
using YoutubeExplode.Videos;

namespace YoutubeDownloader.Core.Resolving;

/// <summary>
/// Video metadata recovered from an external archive.
/// </summary>
internal sealed class RecoveredVideo(
    VideoId id,
    string title,
    string authorName,
    string? authorChannelId,
    string? thumbnailUrl
) : IVideo
{
    public VideoId Id { get; } = id;

    public string Url => $"https://www.youtube.com/watch?v={Id}";

    public string Title { get; } = string.IsNullOrWhiteSpace(title) ? $"Recovered video ({id})" : title;

    public Author Author { get; } = new(
        ChannelId.TryParse(authorChannelId ?? "") ?? new ChannelId("UC0000000000000000000000"),
        string.IsNullOrWhiteSpace(authorName) ? "Unknown channel" : authorName
    );

    public TimeSpan? Duration => null;

    public IReadOnlyList<Thumbnail> Thumbnails { get; } =
    [
        new(
            thumbnailUrl ?? $"https://i.ytimg.com/vi/{id}/hqdefault.jpg",
            new Resolution(480, 360)
        ),
    ];
}
