using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gress;
using Gress.Completable;
using PowerKit;
using PowerKit.Extensions;
using YoutubeDownloader.Core.Downloading;
using YoutubeDownloader.Core.Resolving;
using YoutubeDownloader.Core.Tagging;
using YoutubeDownloader.Framework;
using YoutubeDownloader.Localization;
using YoutubeDownloader.Services;
using YoutubeExplode.Exceptions;
using YoutubeExplode.Videos;

namespace YoutubeDownloader.ViewModels.Components;

public partial class DashboardViewModel
{
    private async Task<bool> TryRecoverUnavailableVideoAsync(string query)
    {
        var videoId = VideoId.TryParse(query);
        if (videoId is null)
            return false;

        var filePath = Path.Combine(Environment.CurrentDirectory, $"{videoId}.mp4");
        return await ArchiveRecovery.TryDownloadAsync(videoId.Value, filePath);
    }
}
