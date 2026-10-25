using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using BKE_MediaTools.CompactUi;

namespace BKE_MediaTools
{
    /// <summary>
    /// Compact Render Dock presentation layer.
    ///
    /// This partial only observes the existing pipeline. It forwards drag/drop to
    /// the existing <c>Form_DragEnter</c>/<c>Form_DragDrop</c> handlers, reads the
    /// existing queue, and receives lifecycle callbacks from the existing runner.
    /// It never enqueues, dequeues, schedules, runs FFmpeg, writes files, or
    /// touches licensing.
    /// </summary>
    public partial class BKE_RenderDock
    {
        private static readonly string[] OutputExtensions = { ".mp4", ".jpg" };

        private readonly DockJobFeed _jobFeed = new();
        private CompactDockView? _dock;

        private void InitializeCompactUi()
        {
            SuspendLayout();

            // The compact dock paints its own dark surface; the legacy stretched
            // background image is no longer visible behind it.
            BackgroundImage = null;
            BackColor = DockTheme.Window;
            ClientSize = new Size(392, 580);
            MinimumSize = SizeFromClientSize(new Size(340, 500));

            _dock = new CompactDockView(_jobFeed) { Dock = DockStyle.Fill };
            _dock.DragEnter += CompactDock_DragEnter;
            _dock.DragLeave += (_, __) => _dock.SetDragState(false, null);
            _dock.DragDrop += CompactDock_DragDrop;

            // Preserve the existing Ctrl+double-click shortcut on the new surface.
            _dock.MouseDoubleClick += (_, e) => BKE_RenderDock_MouseDoubleClick(this, e);

            _dock.OpenOutputsRequested += (_, __) => OpenOutputRoot();
            _dock.PlayRequested += (_, job) => PlayOutput(job);
            _dock.ShowInFolderRequested += (_, job) => ShowOutputInFolder(job);

            Controls.Add(_dock);
            ResumeLayout(true);
        }

        // ======== Drag & drop (forwarded to the existing handlers) ========

        private void CompactDock_DragEnter(object? sender, DragEventArgs e)
        {
            Form_DragEnter(this, e); // existing acceptance rule

            MediaCounts? counts = null;
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths)
            {
                counts = MediaCounts.FromPaths(
                    paths,
                    IsImageFile,
                    IsVideoFile,
                    IsAudioFile,
                    Directory.Exists,
                    Directory.GetFiles); // same top-level read as the existing drop handler
            }

            _dock?.SetDragState(e.Effect != DragDropEffects.None, counts);
        }

        private void CompactDock_DragDrop(object? sender, DragEventArgs e)
        {
            var counts = _dock?.DragCounts;
            _dock?.SetDragState(false, null);
            long before = _jobFeed.LastSequence;

            Form_DragDrop(this, e); // existing classification, prompts and enqueue

            var added = _jobFeed.AddedSince(before).ToList();
            _dock?.ShowDropSummary(BuildDropSummary(counts, added));
        }

        private static DropSummary BuildDropSummary(MediaCounts? counts, IReadOnlyList<DockJobEntry> added)
        {
            if (added.Count == 0)
            {
                return new DropSummary(counts, "Nothing was queued", "No render job was created from this drop", Added: false);
            }

            string detail = added.Count == 1
                ? $"Detected: {added[0].KindLabel}"
                : $"Detected: {added.Count} jobs · " + string.Join(", ", added.Select(a => a.KindLabel).Distinct());
            return new DropSummary(counts, added.Count == 1 ? "Added to Render Dock" : $"{added.Count} jobs added", detail, Added: true);
        }

        // ======== Queue observation (called by the existing pipeline) ========

        /// <summary>Called on the UI thread after a drop, before the runner is ensured.</summary>
        private void CompactUi_SyncQueuedJobs()
        {
            try
            {
                foreach (var job in _queue.ToArray())
                {
                    _jobFeed.TrackQueued(job, job.Title, DescribeJob(job), job.OutputFolder);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Compact UI queue sync skipped: {ex.Message}");
            }
        }

        /// <summary>Runner callback: a job was dequeued and is starting. Returns the start time used to locate its output.</summary>
        private DateTime CompactUi_JobStarted(WorkItem job)
        {
            var started = DateTime.UtcNow;
            PostToUi(() => _jobFeed.MarkStarted(job, job.Title, DescribeJob(job), job.OutputFolder));
            return started;
        }

        /// <summary>Runner callback: the job finished without throwing.</summary>
        private void CompactUi_JobCompleted(WorkItem job, DateTime startedUtc)
        {
            string? output = FindRecentOutput(job.OutputFolder, startedUtc);
            PostToUi(() => _jobFeed.MarkCompleted(job, output));
        }

        /// <summary>Runner callback: the job threw.</summary>
        private void CompactUi_JobFailed(WorkItem job, Exception error)
        {
            string message = error.Message;
            PostToUi(() => _jobFeed.MarkFailed(job, message));
        }

        private void PostToUi(Action action)
        {
            try
            {
                if (IsDisposed || Disposing || !IsHandleCreated) return;
                if (InvokeRequired) BeginInvoke(action);
                else action();
            }
            catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
            {
                // Window is closing; presentation updates are best-effort.
            }
        }

        private static string DescribeJob(WorkItem job)
        {
            bool soundtrack = !string.IsNullOrWhiteSpace(job.AudioPath);
            string kind = job.Type switch
            {
                JobType.SlideshowFromFolder or JobType.SlideshowFromImages => "Slideshow",
                JobType.SlideshowThenVideos => "Mixed slideshow → videos",
                JobType.CombineVideos => "Combined videos",
                JobType.AddAudioToVideo => "Video with soundtrack",
                JobType.TranscodeSingleVideo => "Transcode to MP4",
                JobType.SaveProcessedImage => "Processed image",
                _ => job.Type.ToString(),
            };

            return soundtrack && job.Type != JobType.AddAudioToVideo ? kind + ", with soundtrack" : kind;
        }

        /// <summary>
        /// Read-only lookup of the file the job just wrote: the newest .mp4/.jpg in the
        /// job's output folder written since the job started. Jobs run one at a time,
        /// so this is the job's own output. Returns null when it cannot be determined.
        /// </summary>
        private static string? FindRecentOutput(string folder, DateTime startedUtc)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return null;
                return new DirectoryInfo(folder)
                    .EnumerateFiles()
                    .Where(f => OutputExtensions.Contains(f.Extension.ToLowerInvariant()))
                    .Where(f => f.LastWriteTimeUtc >= startedUtc.AddSeconds(-2))
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .Select(f => f.FullName)
                    .FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        // ======== Notice actions (shell only; no file writes) ========

        private void PlayOutput(DockJobEntry job)
        {
            if (job.OutputPath == null || !File.Exists(job.OutputPath))
            {
                ShowOutputInFolder(job);
                return;
            }

            TryShell(() => Process.Start(new ProcessStartInfo(job.OutputPath) { UseShellExecute = true }));
        }

        private void ShowOutputInFolder(DockJobEntry job)
        {
            if (job.OutputPath != null && File.Exists(job.OutputPath))
            {
                TryShell(() => Process.Start("explorer.exe", $"/select,\"{job.OutputPath}\""));
            }
            else if (job.OutputFolder != null && Directory.Exists(job.OutputFolder))
            {
                TryShell(() => Process.Start("explorer.exe", $"\"{job.OutputFolder}\""));
            }
            else
            {
                OpenOutputRoot();
            }
        }

        private static void OpenOutputRoot()
        {
            // Same behaviour as the existing Ctrl+double-click shortcut.
            Directory.CreateDirectory(AppConfig.OutputRoot);
            TryShell(() => Process.Start("explorer.exe", AppConfig.OutputRoot));
        }

        private static void TryShell(Func<Process?> start)
        {
            try { start(); }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Render Dock", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
