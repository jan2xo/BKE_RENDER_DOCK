using System.Drawing;
using System.Drawing.Imaging;
using BKE_MediaTools.CompactUi;

// Renders each compact-dock state with the production DockPainter.
// Usage: dotnet run -- <output-directory> [scale]
string outDir = args.Length > 0 ? args[0] : "compact-ui-previews";
float scale = args.Length > 1 ? float.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture) : 1f;
Directory.CreateDirectory(outDir);
var client = new Size((int)(392 * scale), (int)(580 * scale));

// Fixture jobs (preview data only — the app builds these from its real queue).
var w1 = new object(); var w2 = new object(); var w3 = new object(); var w4 = new object();

void Render(string name, DockFrame frame)
{
    using var bmp = new Bitmap(client.Width, client.Height, PixelFormat.Format32bppArgb);
    bmp.SetResolution(96 * scale, 96 * scale);
    using (var g = Graphics.FromImage(bmp))
    {
        var layout = DockLayout.Compute(client, scale, frame.Jobs.Count, frame.Notice != null);
        DockPainter.Paint(g, client, scale, frame, layout, paintButtons: true);
    }
    var path = Path.Combine(outDir, name + ".png");
    bmp.Save(path, ImageFormat.Png);
    Console.WriteLine(path);
}

Render("01-idle-empty", new DockFrame());

Render("02-drag-over", new DockFrame
{
    DragActive = true,
    DragCounts = new MediaCounts(14, 3, 1),
});

Render("03-drag-over-unsupported", new DockFrame { DragActive = true, DragCounts = new MediaCounts(0, 0, 0) });

var feed = new DockJobFeed();
feed.TrackQueued(w1, "Brand Launch Slideshow", "Mixed slideshow → videos, with soundtrack", null);
feed.TrackQueued(w2, "Event Highlights", "Combined videos", null);
Render("04-post-drop", new DockFrame
{
    Jobs = feed.Ordered(),
    Flash = new DropSummary(new MediaCounts(14, 3, 1), "Added to Render Dock", "Detected: Mixed slideshow → videos, with soundtrack", true),
});

feed.MarkStarted(w1, "Brand Launch Slideshow", "Mixed slideshow → videos, with soundtrack", null);
Render("05-rendering-queued", new DockFrame { Jobs = feed.Ordered(), Phase = 0.45f });

feed.MarkCompleted(w1, @"D:\BKE_RENDER_DOCK\10-26-2026\Brand Launch Slideshow.mp4");
feed.MarkStarted(w2, "Event Highlights", "Combined videos", null);
Render("06-completed", new DockFrame { Jobs = feed.Ordered(), Notice = feed.Notice, Phase = 0.7f });

feed.DismissNotice();
feed.MarkFailed(w2, "FFmpeg failed. Args: -y -nostdin -i \"clip.mov\" ...\nstderr");
Render("07-failed", new DockFrame { Jobs = feed.Ordered(), Notice = feed.Notice });

feed.DismissNotice();
for (int i = 0; i < 6; i++) feed.TrackQueued(new object(), $"Client Reel Part {i + 1} — extended director's cut with a very long title", "Transcode to MP4", null);
Render("08-overflow-long-names", new DockFrame { Jobs = feed.Ordered() });

Render("09-nothing-queued", new DockFrame
{
    Flash = new DropSummary(new MediaCounts(0, 0, 2), "Nothing was queued", "Only audio was dropped, or the prompt was cancelled", false),
});
