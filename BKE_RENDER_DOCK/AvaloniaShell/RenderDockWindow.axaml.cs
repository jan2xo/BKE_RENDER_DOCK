using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BKE_MediaTools.AvaloniaShell
{
    internal partial class RenderDockWindow : Avalonia.Controls.Window, IRenderDockInteraction
    {
        private readonly ConcurrentQueue<RenderJob> _queue = new();
        private CancellationTokenSource? _cts;
        private bool _isWorking;

        public RenderDockWindow()
        {
            InitializeComponent();

            Directory.CreateDirectory(RenderEngine.OutputRoot);
            Directory.CreateDirectory(RenderEngine.TempRoot);

            DragDrop.AddDragOverHandler(DropZone, OnDragOver);
            DragDrop.AddDropHandler(DropZone, OnDrop);
            Closed += (_, _) => _cts?.Cancel();
        }

        private void OnDragOver(object? sender, Avalonia.Input.DragEventArgs e)
        {
            e.DragEffects = e.DataTransfer.Formats.Contains(DataFormat.File)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
        }

        private async void OnDrop(object? sender, Avalonia.Input.DragEventArgs e)
        {
            try
            {
                var dropped = e.DataTransfer.TryGetFiles()?
                    .Select(item => item.Path.LocalPath)
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .ToArray();

                if (dropped == null || dropped.Length == 0)
                {
                    return;
                }

                var jobs = await RenderJobPlanner.PlanAsync(dropped, this);
                foreach (var job in jobs)
                {
                    _queue.Enqueue(job);
                }

                EnsureRunner();
            }
            catch (Exception ex)
            {
                await ShowMessageAsync(ex.Message, "Drop Error");
            }
        }

        private async Task ProcessQueueAsync(CancellationToken cancellationToken)
        {
            while (_queue.TryDequeue(out var job))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    SetStatus($"Rendering: {job.Title} ({job.Type})");
                    await RenderEngine.ExecuteAsync(job, cancellationToken);
                    SetStatus($"Completed: {job.Title}");
                }
                catch (OperationCanceledException)
                {
                    SetStatus("Rendering cancelled.");
                    return;
                }
                catch (Exception ex)
                {
                    SetStatus($"Failed: {job.Title}");
                    await ShowMessageAsync(
                        $"Job failed: {ex.Message}",
                        "Render Dock");
                }
            }

            SetStatus("All queued renders are done.");
        }

        private void EnsureRunner()
        {
            if (_isWorking)
            {
                return;
            }

            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            _isWorking = true;

            _ = RunQueueAsync(_cts.Token);
        }

        private async Task RunQueueAsync(CancellationToken cancellationToken)
        {
            try
            {
                await ProcessQueueAsync(cancellationToken);
            }
            finally
            {
                _isWorking = false;

                if (!_queue.IsEmpty && !cancellationToken.IsCancellationRequested)
                {
                    EnsureRunner();
                }
            }
        }

        private void SetStatus(string message)
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                StatusText.Text = message;
            }
            else
            {
                Dispatcher.UIThread.Post(() => StatusText.Text = message);
            }
        }

        private void OpenOutput_Click(object? sender, RoutedEventArgs e)
        {
            Directory.CreateDirectory(RenderEngine.OutputRoot);
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{RenderEngine.OutputRoot}\"",
                UseShellExecute = true
            });
        }

        public Task<string?> PromptAsync(string text, string caption) =>
            AvaloniaDialogService.PromptAsync(this, caption, text);

        public Task<string?> PromptForAudioFileAsync() =>
            AvaloniaDialogService.PickAudioFileAsync(this);

        async Task<LoopPolicy> IRenderDockInteraction.GetLoopPolicyAsync()
        {
            if (!RenderEngine.PromptForLoopPolicy)
            {
                return RenderEngine.DefaultLoopPolicy;
            }

            var choice = await AvaloniaDialogService.ChooseAsync(
                this,
                "Audio Mode",
                "Choose how video and audio duration should be reconciled.",
                "Loop video to audio",
                "Loop audio to video",
                "Shortest");

            return choice switch
            {
                DialogChoice.Primary => LoopPolicy.LoopVideoToAudio,
                DialogChoice.Secondary => LoopPolicy.LoopAudioToVideo,
                _ => LoopPolicy.Shortest
            };
        }

        async Task<DetectedAudioChoice> IRenderDockInteraction.ChooseDetectedAudioAsync(
            IReadOnlyList<string> audios,
            string? bestAudio)
        {
            var choice = await AvaloniaDialogService.ChooseAsync(
                this,
                "Add background audio?",
                $"Found {audios.Count} audio file(s). Best match: {Path.GetFileName(bestAudio)}",
                "Use best match",
                "Browse",
                "Cancel");

            return choice switch
            {
                DialogChoice.Primary => DetectedAudioChoice.UseBest,
                DialogChoice.Secondary => DetectedAudioChoice.Browse,
                _ => DetectedAudioChoice.Cancel
            };
        }

        public Task<bool> OfferAudioFileAsync() =>
            AvaloniaDialogService.ConfirmAsync(
                this,
                "Add audio?",
                "No audio was detected. Browse for one?",
                "Browse",
                "No");

        public Task ShowMessageAsync(string text, string caption) =>
            AvaloniaDialogService.ShowMessageAsync(this, caption, text);
    }
}
