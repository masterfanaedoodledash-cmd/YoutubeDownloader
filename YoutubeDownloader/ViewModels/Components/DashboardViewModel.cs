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

public partial class DashboardViewModel : ViewModelBase
{
    private readonly ViewModelManager _viewModelManager;
    private readonly SnackbarManager _snackbarManager;
    private readonly DialogManager _dialogManager;
    private readonly LocalizationManager _localizationManager;
    private readonly SettingsService _settingsService;

    private readonly IDisposable _eventSubscription;
    private readonly ResizableSemaphore _downloadSemaphore = new();
    private readonly AutoResetProgressMuxer _progressMuxer;

    public DashboardViewModel(
        ViewModelManager viewModelManager,
        SnackbarManager snackbarManager,
        DialogManager dialogManager,
        LocalizationManager localizationManager,
        SettingsService settingsService
    )
    {
        _viewModelManager = viewModelManager;
        _snackbarManager = snackbarManager;
        _dialogManager = dialogManager;
        _localizationManager = localizationManager;
        LocalizationManager = localizationManager;
        _settingsService = settingsService;

        _progressMuxer = Progress.CreateMuxer().WithAutoReset();

        _eventSubscription = Disposable.Merge(
            _settingsService.WatchProperty(
                o => o.ParallelLimit,
                v => _downloadSemaphore.MaxCount = v,
                true
            ),
            Progress.WatchProperty(
                o => o.Current,
                _ => OnPropertyChanged(nameof(IsProgressIndeterminate))
            )
        );
    }

    public LocalizationManager LocalizationManager { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProgressIndeterminate))]
    [NotifyCanExecuteChangedFor(nameof(ProcessQueryCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShowAuthSetupCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShowSettingsCommand))]
    public partial bool IsBusy { get; set; }

    public ProgressContainer<Percentage> Progress { get; } = new();

    public bool IsProgressIndeterminate => IsBusy && Progress.Current.Fraction is <= 0 or >= 1;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ProcessQueryCommand))]
    public partial string? Query { get; set; }

    public ObservableCollection<DownloadViewModel> Downloads { get; } = [];

    private async Task EnsureFFmpegAsync()
    {
        if (_settingsService.FFmpegFilePath is not null)
            return;

        if (FFmpeg.TryGetCliFilePath() is not null)
            return;

        var dialog = _viewModelManager.GetMessageBoxViewModel(
            _localizationManager.FFmpegMissingTitle,
            string.Format(_localizationManager.FFmpegMissingMessage, Program.Name),
            _localizationManager.DownloadButton,
            _localizationManager.CloseButton
        );

        if (await _dialogManager.ShowDialogAsync(dialog) != true)
        {
            await _dialogManager.ShowDialogAsync(_viewModelManager.GetSettingsViewModel());
            return;
        }

        IsBusy = true;
        var progress = _progressMuxer.CreateInput();
        _snackbarManager.Notify(_localizationManager.FFmpegDownloadingTitle);

        try
        {
            await FFmpeg.DownloadAsync(
                Path.Combine(AppContext.BaseDirectory, FFmpeg.CliFileName),
                progress
            );

            _snackbarManager.Notify(_localizationManager.FFmpegDownloadCompletedTitle);
        }
        catch (Exception ex)
        {
            await _dialogManager.ShowDialogAsync(
                _viewModelManager.GetMessageBoxViewModel(_localizationManager.ErrorTitle, ex.Message)
            );

            App.Shutdown(3);
        }
        finally
        {
            progress.ReportCompletion();
            IsBusy = false;
        }
    }

    public override async Task InitializeAsync() => await EnsureFFmpegAsync();

    private bool CanShowAuthSetup() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanShowAuthSetup))]
    private async Task ShowAuthSetupAsync() =>
        await _dialogManager.ShowDialogAsync(_viewModelManager.GetAuthSetupViewModel());

    private bool CanShowSettings() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanShowSettings))]
    private async Task ShowSettingsAsync() =>
        await _dialogManager.ShowDialogAsync(_viewModelManager.GetSettingsViewModel());

    private async void EnqueueDownload(DownloadViewModel download, int position = 0)
    {
        Downloads.Insert(position, download);
        var progress = _progressMuxer.CreateInput();

        try
        {
            using var downloader = new VideoDownloader(_settingsService.LastAuthCookies);
            var tagInjector = new MediaTagInjector();

            using var access = await _downloadSemaphore.AcquireAsync(download.CancellationToken);

            download.Status = DownloadStatus.Started;

            var downloadOption =
                download.DownloadOption
                ?? await downloader.GetBestDownloadOptionAsync(
                    download.Video!.Id,
                    download.DownloadPreference!,
                    _settingsService.ShouldInjectLanguageSpecificAudioStreams,
                    download.CancellationToken
                );

            await downloader.DownloadVideoAsync(
                download.FilePath!,
                download.Video!,
                downloadOption,
                _settingsService.ShouldInjectSubtitles,
                _settingsService.FFmpegFilePath,
                download.Progress.Merge(progress),
                download.CancellationToken
            );

            if (_settingsService.ShouldInjectTags)
            {
                try
                {
                    await tagInjector.InjectTagsAsync(
                        download.FilePath!,
                        download.Video!,
                        download.CancellationToken
                    );
                }
                catch
                {
                    // Media tagging is not critical
                }
            }

            download.Status = DownloadStatus.Completed;
        }
        catch (Exception ex)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(download.FilePath))
                    File.Delete(download.FilePath);
            }
            catch
            {
                // Ignore
            }

            download.Status =
                ex is OperationCanceledException ? DownloadStatus.Canceled : DownloadStatus.Failed;
            download.ErrorMessage = ex is YoutubeExplodeException ? ex.Message : ex.ToString();
        }
        finally
        {
            progress.ReportCompletion();
            download.Dispose();
        }
    }

    private bool CanProcessQuery() => !IsBusy && !string.IsNullOrWhiteSpace(Query);

    [RelayCommand(CanExecute = nameof(CanProcessQuery))]
    private async Task ProcessQueryAsync()
    {
        if (string.IsNullOrWhiteSpace(Query))
            return;

        IsBusy = true;
        var progress = _progressMuxer.CreateInput(0.01);

        try
        {
            using var resolver = new QueryResolver(_settingsService.LastAuthCookies);
            var queries = Query.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            );
            var queryResults = new List<QueryResult>();

            foreach (var (i, query) in queries.Index())
            {
                try
                {
                    queryResults.Add(await resolver.ResolveAsync(query));
                }
                catch (VideoUnavailableException) when (queries.Length == 1)
                {
                    var videoId = VideoId.TryParse(query);
                    if (videoId is null)
                        throw;

                    var filePath = Path.Combine(
                        Environment.CurrentDirectory,
                        $"{videoId.Value}.mp4"
                    );

                    if (
                        await ArchiveRecovery.TryDownloadAsync(
                            videoId.Value,
                            filePath
                        )
                    )
                    {
                        Query = "";
                        return;
                    }

                    throw new InvalidOperationException(
                        $"No archive or mirror found for video {videoId.Value}."
                    );
                }
                catch (YoutubeExplodeException ex)
                    when (ex is VideoUnavailableException or PlaylistUnavailableException
                        && queries.Length > 1
                    )
                {
                    _snackbarManager.Notify(ex.Message);
                }

                progress.Report(Percentage.FromFraction((i + 1.0) / queries.Length));
            }

            var queryResult = QueryResult.Aggregate(queryResults);

            if (queryResult.Videos.Count == 1)
            {
                var video = queryResult.Videos.Single();
                using var downloader = new VideoDownloader(_settingsService.LastAuthCookies);
                var downloadOptions = await downloader.GetDownloadOptionsAsync(
                    video.Id,
                    _settingsService.ShouldInjectLanguageSpecificAudioStreams
                );
                var download = await _dialogManager.ShowDialogAsync(
                    _viewModelManager.GetDownloadSingleSetupViewModel(video, downloadOptions)
                );

                if (download is null)
                    return;

                EnqueueDownload(download);
                Query = "";
            }
            else if (queryResult.Videos.Count > 1)
            {
                var downloads = await _dialogManager.ShowDialogAsync(
                    _viewModelManager.GetDownloadMultipleSetupViewModel(
                        queryResult.Title,
                        queryResult.Videos,
                        queryResult.Kind is not QueryResultKind.Search
                            and not QueryResultKind.Aggregate
                    )
                );

                if (downloads is null)
                    return;

                foreach (var download in downloads)
                    EnqueueDownload(download);

                Query = "";
            }
            else
            {
                await _dialogManager.ShowDialogAsync(
                    _viewModelManager.GetMessageBoxViewModel(
                        LocalizationManager.NothingFoundTitle,
                        LocalizationManager.NothingFoundMessage
                    )
                );
            }
        }
        catch (Exception ex)
        {
            await _dialogManager.ShowDialogAsync(
                _viewModelManager.GetMessageBoxViewModel(
                    LocalizationManager.ErrorTitle,
                    ex is YoutubeExplodeException ? ex.Message : ex.Message
                )
            );
        }
        finally
        {
            progress.ReportCompletion();
            IsBusy = false;
        }
    }

    private void RemoveDownload(DownloadViewModel download)
    {
        Downloads.Remove(download);
        download.CancelCommand.ExecuteIfCan(null);
        download.Dispose();
    }

    [RelayCommand]
    private void RemoveSuccessfulDownloads()
    {
        foreach (var download in Downloads.ToArray())
        {
            if (download.Status == DownloadStatus.Completed)
                RemoveDownload(download);
        }
    }

    [RelayCommand]
    private void RemoveInactiveDownloads()
    {
        foreach (var download in Downloads.ToArray())
        {
            if (
                download.Status
                is DownloadStatus.Completed
                    or DownloadStatus.Failed
                    or DownloadStatus.Canceled
            )
                RemoveDownload(download);
        }
    }

    [RelayCommand]
    private void RestartDownload(DownloadViewModel download)
    {
        var position = Math.Max(0, Downloads.IndexOf(download));
        RemoveDownload(download);

        var newDownload = download.DownloadOption is not null
            ? _viewModelManager.GetDownloadViewModel(
                download.Video!,
                download.DownloadOption,
                download.FilePath!
            )
            : _viewModelManager.GetDownloadViewModel(
                download.Video!,
                download.DownloadPreference!,
                download.FilePath!
            );

        EnqueueDownload(newDownload, position);
    }

    [RelayCommand]
    private void RestartFailedDownloads()
    {
        foreach (var download in Downloads.ToArray())
        {
            if (download.Status == DownloadStatus.Failed)
                RestartDownload(download);
        }
    }

    [RelayCommand]
    private void CancelAllDownloads()
    {
        foreach (var download in Downloads)
            download.CancelCommand.ExecuteIfCan(null);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            CancelAllDownloads();
            _eventSubscription.Dispose();
            _downloadSemaphore.Dispose();
        }

        base.Dispose(disposing);
    }
}
