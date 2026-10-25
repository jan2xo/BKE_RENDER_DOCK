using System.Text.RegularExpressions;
using BKE_MediaTools.CompactUi;
using Xunit;

namespace RenderDock.Tests;

public sealed class CompactUiFeedTests
{
    private sealed record FakeWorkItem(string Title);

    [Fact]
    public void CountsFormatLikeTheCompactDesign()
    {
        Assert.Equal("14 images · 3 videos · 1 audio", new MediaCounts(14, 3, 1).Format());
        Assert.Equal("1 image · 1 video", new MediaCounts(1, 1, 0).Format());
        Assert.Equal("2 audio", new MediaCounts(0, 0, 2).Format());
        Assert.Equal(string.Empty, new MediaCounts(0, 0, 0).Format());
    }

    [Fact]
    public void CountsUseSuppliedPredicatesAndTopLevelFolderContents()
    {
        var folders = new Dictionary<string, string[]>
        {
            ["/drop/shoot"] = new[] { "/drop/shoot/a.jpg", "/drop/shoot/b.png", "/drop/shoot/clip.mov", "/drop/shoot/notes.txt" },
        };

        var counts = MediaCounts.FromPaths(
            new[] { "/drop/shoot", "/drop/song.mp3", "/drop/a2.jpg", "/drop/a2.jpg" },
            p => p.EndsWith(".jpg") || p.EndsWith(".png"),
            p => p.EndsWith(".mov"),
            p => p.EndsWith(".mp3"),
            p => folders.ContainsKey(p),
            p => folders[p]);

        Assert.Equal(new MediaCounts(3, 1, 1), counts);
    }

    [Fact]
    public void UnreadableFolderIsSkippedInsteadOfThrowing()
    {
        var counts = MediaCounts.FromPaths(
            new[] { "/locked", "/x.jpg" },
            p => p.EndsWith(".jpg"), _ => false, _ => false,
            p => p == "/locked",
            _ => throw new UnauthorizedAccessException());

        Assert.Equal(new MediaCounts(1, 0, 0), counts);
    }

    [Fact]
    public void LifecycleMovesQueuedToRenderingToCompletedAndAnnouncesOnce()
    {
        var feed = new DockJobFeed();
        var job = new FakeWorkItem("Brand Launch");

        Assert.True(feed.TrackQueued(job, "Brand Launch", "Slideshow", "/out"));
        Assert.False(feed.TrackQueued(job, "Brand Launch", "Slideshow", "/out"));
        Assert.Equal(DockJobState.Queued, feed.Ordered().Single().State);

        feed.MarkStarted(job, "Brand Launch", "Slideshow", "/out");
        Assert.Equal(DockJobState.Rendering, feed.Ordered().Single().State);
        Assert.True(feed.HasActiveWork);

        Assert.True(feed.MarkCompleted(job, "/out/Brand Launch.mp4"));
        Assert.False(feed.MarkCompleted(job, "/out/Brand Launch.mp4"));
        Assert.False(feed.MarkFailed(job, "late failure"));

        var entry = feed.Ordered().Single();
        Assert.Equal(DockJobState.Completed, entry.State);
        Assert.Equal("/out/Brand Launch.mp4", entry.OutputPath);
        Assert.Same(entry, feed.Notice);
    }

    [Fact]
    public void FailureIsDistinctFromCompletionAndHidesFfmpegArguments()
    {
        var feed = new DockJobFeed();
        var job = new FakeWorkItem("Event Highlights");
        feed.MarkStarted(job, "Event Highlights", "Combined videos", "/out");

        Assert.True(feed.MarkFailed(job, "FFmpeg failed. Args: -y -i \"secret path.mov\"\nstderr line"));

        var notice = Assert.IsType<DockJobEntry>(feed.Notice);
        Assert.Equal(DockJobState.Failed, notice.State);
        Assert.Equal("FFmpeg failed.", notice.FailureReason);
        Assert.Null(notice.OutputPath);
    }

    [Fact]
    public void IdenticalWorkItemsStayTwoSeparateJobs()
    {
        var feed = new DockJobFeed();
        var first = new FakeWorkItem("Same");
        var second = new FakeWorkItem("Same");
        Assert.Equal(first, second); // records compare by value...

        Assert.True(feed.TrackQueued(first, "Same", "Slideshow", null));
        Assert.True(feed.TrackQueued(second, "Same", "Slideshow", null)); // ...but are tracked by identity
        Assert.Equal(2, feed.Ordered().Count);
    }

    [Fact]
    public void StartedJobUnknownToTheFeedIsStillShown()
    {
        var feed = new DockJobFeed();
        feed.MarkStarted(new object(), "Late", "Transcode to MP4", null);
        Assert.Equal(DockJobState.Rendering, feed.Ordered().Single().State);
    }

    [Fact]
    public void OrderingIsActiveThenQueuedThenRecentFinishedAndFinishedHistoryIsBounded()
    {
        var feed = new DockJobFeed { MaxFinishedKept = 2 };
        var jobs = Enumerable.Range(0, 6).Select(i => new object()).ToArray();
        for (int i = 0; i < jobs.Length; i++) feed.TrackQueued(jobs[i], $"Job {i}", "Slideshow", null);

        for (int i = 0; i < 3; i++)
        {
            feed.MarkStarted(jobs[i], $"Job {i}", "Slideshow", null);
            feed.MarkCompleted(jobs[i], null);
            if (i < 2) feed.DismissNotice();
        }

        feed.MarkStarted(jobs[3], "Job 3", "Slideshow", null);

        var names = feed.Ordered().Select(e => e.Name).ToArray();
        Assert.Equal(new[] { "Job 3", "Job 4", "Job 5", "Job 2", "Job 1" }, names);
    }

    [Fact]
    public void DismissClearsTheNoticeWithoutRemovingTheJobCard()
    {
        var feed = new DockJobFeed();
        var job = new object();
        feed.MarkStarted(job, "A", "Slideshow", null);
        feed.MarkCompleted(job, null);
        feed.DismissNotice();

        Assert.Null(feed.Notice);
        Assert.Equal(DockJobState.Completed, feed.Ordered().Single().State);
    }

    [Fact]
    public void AddedSinceReportsOnlyJobsFromTheLatestDrop()
    {
        var feed = new DockJobFeed();
        feed.TrackQueued(new object(), "Old", "Slideshow", null);
        long before = feed.LastSequence;
        feed.TrackQueued(new object(), "New A", "Combined videos", null);
        feed.TrackQueued(new object(), "New B", "Transcode to MP4", null);

        Assert.Equal(new[] { "New A", "New B" }, feed.AddedSince(before).Select(e => e.Name));
    }
}

public sealed class CompactUiBoundaryContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepositoryRoot }.Concat(parts).ToArray()));

    private static IEnumerable<string> CompactUiSources()
    {
        yield return Read("BKE_RENDER_DOCK", "BKE_RENDER_DOCK.CompactUi.cs");
        foreach (var file in Directory.GetFiles(Path.Combine(RepositoryRoot, "BKE_RENDER_DOCK", "CompactUi"), "*.cs"))
        {
            yield return File.ReadAllText(file);
        }
    }

    [Fact]
    public void CompactUiNeverRunsFfmpegOrMutatesTheQueue()
    {
        foreach (var source in CompactUiSources())
        {
            Assert.DoesNotContain("RunFfmpeg", source, StringComparison.Ordinal);
            Assert.DoesNotContain("FFmpegPath", source, StringComparison.Ordinal);
            Assert.DoesNotContain("_queue.Enqueue", source, StringComparison.Ordinal);
            Assert.DoesNotContain("TryDequeue", source, StringComparison.Ordinal);
            Assert.DoesNotContain("EnsureRunner", source, StringComparison.Ordinal);
            Assert.DoesNotContain("ProcessImage", source, StringComparison.Ordinal);
            Assert.DoesNotContain("File.Move", source, StringComparison.Ordinal);
            Assert.DoesNotContain("File.Delete", source, StringComparison.Ordinal);
            Assert.DoesNotContain("File.Write", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Licensing", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void CompactUiAddsNoWindowLifecyclePolicyOrSecondNotificationSystem()
    {
        foreach (var source in CompactUiSources())
        {
            Assert.DoesNotContain("TopMost", source, StringComparison.Ordinal);
            Assert.DoesNotContain("FormWindowState.Minimized", source, StringComparison.Ordinal);
            Assert.DoesNotContain("ShowInTaskbar", source, StringComparison.Ordinal);
            Assert.DoesNotContain("new NotifyIcon", source, StringComparison.Ordinal);
            Assert.DoesNotContain("FormBorderStyle", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void DragOverHighlightIsYellow()
    {
        var theme = Read("BKE_RENDER_DOCK", "CompactUi", "DockTheme.cs");
        var painter = Read("BKE_RENDER_DOCK", "CompactUi", "DockPainter.cs");

        Assert.Contains("DragYellow = Color.FromArgb(250, 204, 21)", theme);
        Assert.Matches(new Regex(@"new Pen\(DockTheme\.DragYellow"), painter);
    }

    [Fact]
    public void ExistingDropHandlersAreReusedNotReplaced()
    {
        var compact = Read("BKE_RENDER_DOCK", "BKE_RENDER_DOCK.CompactUi.cs");
        var form = Read("BKE_RENDER_DOCK", "BKE_RENDER_DOCK.cs");

        Assert.Contains("Form_DragEnter(this, e)", compact);
        Assert.Contains("Form_DragDrop(this, e)", compact);
        Assert.Contains("BKE_RenderDock_MouseDoubleClick(this, e)", compact);
        Assert.Contains("DragEnter += Form_DragEnter;", form);
        Assert.Contains("DragDrop += Form_DragDrop;", form);
        Assert.Contains("_notify.ShowBalloonTip(ms);", form);
        Assert.Contains("MessageBox.Show($\"Job failed: {ex.Message}\"", form);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "BKE_RENDER_DOCK.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Render Dock repository root.");
    }
}
