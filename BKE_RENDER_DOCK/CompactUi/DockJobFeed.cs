using System;
using System.Collections.Generic;
using System.Linq;

namespace BKE_MediaTools.CompactUi
{
    /// <summary>
    /// Presentation state of one render job as shown in the compact dock.
    /// Mirrors the lifecycle the existing job runner already goes through
    /// (enqueued, dequeued/started, finished, threw) — it never drives it.
    /// </summary>
    internal enum DockJobState
    {
        Queued,
        Rendering,
        Completed,
        Failed,
    }

    internal sealed class DockJobEntry
    {
        internal DockJobEntry(object key, long sequence, string name, string kindLabel, string? outputFolder)
        {
            Key = key;
            Sequence = sequence;
            Name = name;
            KindLabel = kindLabel;
            OutputFolder = outputFolder;
        }

        public object Key { get; }
        public long Sequence { get; }
        public string Name { get; }
        public string KindLabel { get; }
        public string? OutputFolder { get; }
        public DockJobState State { get; internal set; } = DockJobState.Queued;
        public string? OutputPath { get; internal set; }
        public string? FailureReason { get; internal set; }
        public DateTime? FinishedUtc { get; internal set; }
        public bool IsFinished => State is DockJobState.Completed or DockJobState.Failed;
        internal bool NoticeRaised { get; set; }
    }

    /// <summary>
    /// UI-only adapter over the existing render queue. Jobs are keyed by the
    /// identity of the runner's own work item (reference equality, because the
    /// work item is a record and two identical drops must stay two cards).
    /// All members are expected to be called on the UI thread.
    /// </summary>
    internal sealed class DockJobFeed
    {
        private readonly Dictionary<object, DockJobEntry> _byKey = new(ReferenceEqualityComparer.Instance);
        private readonly List<DockJobEntry> _entries = new();
        private long _sequence;

        /// <summary>How many finished (completed or failed) cards stay in the list.</summary>
        public int MaxFinishedKept { get; init; } = 3;

        public long LastSequence => _sequence;

        /// <summary>The single completion/failure notice currently on screen, if any.</summary>
        public DockJobEntry? Notice { get; private set; }

        public event EventHandler? Changed;

        /// <summary>Starts tracking a queued job. Returns false when the job is already known.</summary>
        public bool TrackQueued(object key, string name, string kindLabel, string? outputFolder)
        {
            if (_byKey.ContainsKey(key)) return false;
            Add(key, name, kindLabel, outputFolder);
            OnChanged();
            return true;
        }

        /// <summary>The runner dequeued the job and started it.</summary>
        public void MarkStarted(object key, string name, string kindLabel, string? outputFolder)
        {
            if (!_byKey.TryGetValue(key, out var entry))
            {
                entry = Add(key, name, kindLabel, outputFolder);
            }

            if (entry.IsFinished) return;
            entry.State = DockJobState.Rendering;
            OnChanged();
        }

        /// <summary>
        /// The runner finished the job without throwing. Returns true only the
        /// first time, so a completion is announced exactly once.
        /// </summary>
        public bool MarkCompleted(object key, string? outputPath)
        {
            if (!_byKey.TryGetValue(key, out var entry) || entry.IsFinished) return false;
            entry.State = DockJobState.Completed;
            entry.OutputPath = outputPath;
            return Finish(entry);
        }

        /// <summary>The runner caught an exception for the job. Returns true only the first time.</summary>
        public bool MarkFailed(object key, string? reason)
        {
            if (!_byKey.TryGetValue(key, out var entry) || entry.IsFinished) return false;
            entry.State = DockJobState.Failed;
            entry.FailureReason = ShortReason(reason);
            return Finish(entry);
        }

        public void DismissNotice()
        {
            if (Notice == null) return;
            Notice = null;
            Prune();
            OnChanged();
        }

        public IEnumerable<DockJobEntry> AddedSince(long sequence) =>
            _entries.Where(e => e.Sequence > sequence).OrderBy(e => e.Sequence);

        /// <summary>Active first, then queued in run order, then the most recent finished jobs.</summary>
        public IReadOnlyList<DockJobEntry> Ordered()
        {
            return _entries.Where(e => e.State == DockJobState.Rendering).OrderBy(e => e.Sequence)
                .Concat(_entries.Where(e => e.State == DockJobState.Queued).OrderBy(e => e.Sequence))
                .Concat(_entries.Where(e => e.IsFinished).OrderByDescending(e => e.FinishedUtc).ThenByDescending(e => e.Sequence))
                .ToList();
        }

        public bool HasActiveWork => _entries.Any(e => e.State == DockJobState.Rendering);

        public static string ShortReason(string? message)
        {
            if (string.IsNullOrWhiteSpace(message)) return "The render did not finish.";
            var line = message.Replace("\r", string.Empty).Split('\n')[0].Trim();
            int args = line.IndexOf(" Args:", StringComparison.Ordinal);
            if (args > 0) line = line.Substring(0, args).Trim();
            if (line.Length > 120) line = line.Substring(0, 119).TrimEnd() + "…";
            return line;
        }

        private DockJobEntry Add(object key, string name, string kindLabel, string? outputFolder)
        {
            var entry = new DockJobEntry(key, ++_sequence, name, kindLabel, outputFolder);
            _byKey[key] = entry;
            _entries.Add(entry);
            return entry;
        }

        private bool Finish(DockJobEntry entry)
        {
            entry.FinishedUtc = DateTime.UtcNow;
            bool announce = !entry.NoticeRaised;
            if (announce)
            {
                entry.NoticeRaised = true;
                Notice = entry;
            }

            Prune();
            OnChanged();
            return announce;
        }

        private void Prune()
        {
            var finished = _entries.Where(e => e.IsFinished)
                .OrderByDescending(e => e.FinishedUtc).ThenByDescending(e => e.Sequence)
                .ToList();
            foreach (var stale in finished.Skip(MaxFinishedKept))
            {
                if (ReferenceEquals(stale, Notice)) continue;
                _entries.Remove(stale);
                _byKey.Remove(stale.Key);
            }
        }

        private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Counts of supplied media, classified with the application's existing extension rules.</summary>
    internal readonly record struct MediaCounts(int Images, int Videos, int Audio)
    {
        public int Total => Images + Videos + Audio;

        public string Format()
        {
            var parts = new List<string>(3);
            if (Images > 0) parts.Add($"{Images} {(Images == 1 ? "image" : "images")}");
            if (Videos > 0) parts.Add($"{Videos} {(Videos == 1 ? "video" : "videos")}");
            if (Audio > 0) parts.Add($"{Audio} audio");
            return string.Join(" · ", parts);
        }

        /// <summary>
        /// Counts what a drop supplies: dropped files, plus the top-level files of
        /// dropped folders (the same depth the existing drop handler reads).
        /// Classification is delegated to the application's own predicates.
        /// </summary>
        public static MediaCounts FromPaths(
            IEnumerable<string> paths,
            Func<string, bool> isImage,
            Func<string, bool> isVideo,
            Func<string, bool> isAudio,
            Func<string, bool> isDirectory,
            Func<string, IEnumerable<string>> listFiles)
        {
            int images = 0, videos = 0, audio = 0;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Count(string file)
            {
                if (!seen.Add(file)) return;
                if (isImage(file)) images++;
                else if (isVideo(file)) videos++;
                else if (isAudio(file)) audio++;
            }

            foreach (var path in paths)
            {
                if (string.IsNullOrWhiteSpace(path)) continue;
                if (isDirectory(path))
                {
                    IEnumerable<string> files;
                    try { files = listFiles(path); }
                    catch { continue; }
                    foreach (var file in files) Count(file);
                }
                else
                {
                    Count(path);
                }
            }

            return new MediaCounts(images, videos, audio);
        }
    }
}
