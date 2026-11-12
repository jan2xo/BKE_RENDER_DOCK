using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace BKE_MediaTools.CompactUi
{
    /// <summary>Everything the compact dock needs to draw one frame.</summary>
    internal sealed class DockFrame
    {
        public bool DragActive { get; init; }
        public MediaCounts? DragCounts { get; init; }

        /// <summary>Short-lived post-drop summary built from what was actually queued.</summary>
        public DropSummary? Flash { get; init; }

        public IReadOnlyList<DockJobEntry> Jobs { get; init; } = Array.Empty<DockJobEntry>();
        public DockJobEntry? Notice { get; init; }

        /// <summary>0..1 phase for the indeterminate "rendering" indicator.</summary>
        public float Phase { get; init; }
    }

    internal sealed record DropSummary(MediaCounts? Counts, string Headline, string? Detail, bool Added);

    /// <summary>Pixel layout for a given client size and scale (DPI / 96).</summary>
    internal sealed class DockLayout
    {
        public Rectangle Header;
        public Rectangle OutputsButton;
        public Rectangle DropZone;
        public Rectangle QueueLabel;
        public Rectangle QueueArea;
        public Rectangle Notice;
        public Rectangle PlayButton;
        public Rectangle FolderButton;
        public Rectangle DismissButton;
        public readonly List<Rectangle> Cards = new();
        public int HiddenJobs;
        public Rectangle MoreLabel;

        public static DockLayout Compute(Size client, float s, int jobCount, bool hasNotice)
        {
            int S(float v) => (int)Math.Round(v * s);
            var l = new DockLayout();
            int pad = S(16);
            int w = client.Width;
            int h = client.Height;

            l.Header = new Rectangle(0, 0, w, S(52));
            l.OutputsButton = new Rectangle(w - pad - S(30), S(11), S(30), S(30));

            int dropTop = l.Header.Bottom + S(14);
            int dropHeight = Math.Max(S(150), Math.Min(S(196), (int)(h * 0.34)));
            l.DropZone = new Rectangle(pad, dropTop, w - 2 * pad, dropHeight);

            l.QueueLabel = new Rectangle(pad, l.DropZone.Bottom + S(16), w - 2 * pad, S(18));

            int bottom = h - pad;
            if (hasNotice)
            {
                l.Notice = new Rectangle(pad, bottom - S(102), w - 2 * pad, S(102));
                l.DismissButton = new Rectangle(l.Notice.Right - S(32), l.Notice.Top + S(8), S(24), S(24));
                int bx = l.Notice.Left + S(64);
                int by = l.Notice.Bottom - S(14) - S(30);
                l.PlayButton = new Rectangle(bx, by, S(64), S(30));
                l.FolderButton = new Rectangle(l.PlayButton.Right + S(8), by, S(124), S(30));
                bottom = l.Notice.Top - S(12);
            }

            l.QueueArea = Rectangle.FromLTRB(pad, l.QueueLabel.Bottom + S(8), w - pad, Math.Max(l.QueueLabel.Bottom + S(8), bottom));

            int cardH = S(62);
            int gap = S(8);
            int moreH = S(20);
            int y = l.QueueArea.Top;
            int fit = 0;
            for (int i = 0; i < jobCount; i++)
            {
                bool last = i == jobCount - 1;
                int needed = cardH + (last ? 0 : moreH + gap);
                if (y + needed > l.QueueArea.Bottom) break;
                l.Cards.Add(new Rectangle(l.QueueArea.Left, y, l.QueueArea.Width, cardH));
                y += cardH + gap;
                fit++;
            }

            l.HiddenJobs = jobCount - fit;
            if (l.HiddenJobs > 0)
            {
                l.MoreLabel = new Rectangle(l.QueueArea.Left, y - S(2), l.QueueArea.Width, moreH);
            }

            return l;
        }
    }

    internal enum DockButtonKind { Primary, Secondary, Icon }

    /// <summary>
    /// Pure System.Drawing renderer for the compact dock. Shared by the WinForms
    /// control and by the preview renderer so screenshots show this exact code.
    /// </summary>
    internal static class DockPainter
    {
        public static void Paint(Graphics g, Size client, float s, DockFrame frame, DockLayout l, bool paintButtons)
        {
            Prepare(g);
            g.Clear(DockTheme.Window);

            PaintHeader(g, l, s);
            if (paintButtons) PaintIconButton(g, l.OutputsButton, s, hover: false, focused: false, glyph: Glyph.Folder);

            PaintDropZone(g, l.DropZone, s, frame);
            PaintQueue(g, l, s, frame);

            if (frame.Notice != null)
            {
                PaintNotice(g, l, s, frame.Notice);
                if (paintButtons) PaintNoticeButtons(g, l, s, frame.Notice);
            }
        }

        public static void Prepare(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        }

        // ---------------------------------------------------------------- header

        private static void PaintHeader(Graphics g, DockLayout l, float s)
        {
            using (var fill = new SolidBrush(DockTheme.HeaderFill)) g.FillRectangle(fill, l.Header);
            using (var line = new Pen(DockTheme.Divider, Math.Max(1f, s))) g.DrawLine(line, 0, l.Header.Bottom - 1, l.Header.Right, l.Header.Bottom - 1);

            float x = 16 * s;
            float cy = l.Header.Height / 2f;
            var bolt = new RectangleF(x, cy - 8 * s, 11 * s, 16 * s);
            using (var accent = new LinearGradientBrush(bolt, DockTheme.AccentStart, DockTheme.AccentEnd, LinearGradientMode.Vertical))
            {
                FillBolt(g, accent, bolt);
            }

            x = bolt.Right + 4 * s;
            using var brand = DockTheme.Font(9f, s, FontStyle.Bold);
            using var title = DockTheme.Font(10.5f, s, FontStyle.Bold);
            using (var brandBrush = new SolidBrush(DockTheme.AccentEnd))
            {
                var size = g.MeasureString("BKE", brand, PointF.Empty, StringFormat.GenericTypographic);
                g.DrawString("BKE", brand, brandBrush, x, cy - size.Height / 2f, StringFormat.GenericTypographic);
                x += size.Width + 9 * s;
            }

            using var titleBrush = new SolidBrush(DockTheme.Text);
            var tsize = g.MeasureString("Render Dock", title, PointF.Empty, StringFormat.GenericTypographic);
            g.DrawString("Render Dock", title, titleBrush, x, cy - tsize.Height / 2f, StringFormat.GenericTypographic);
        }

        // ------------------------------------------------------------- drop zone

        private static void PaintDropZone(Graphics g, Rectangle r, float s, DockFrame f)
        {
            float radius = 12 * s;
            if (f.DragActive)
            {
                // Soft yellow glow outside the border, then a crisp yellow edge.
                for (int i = 4; i >= 1; i--)
                {
                    var glow = Rectangle.Inflate(r, (int)(i * 1.6f * s), (int)(i * 1.6f * s));
                    using var gp = RoundRect(glow, radius + i * 1.6f * s);
                    using var pen = new Pen(Color.FromArgb(14 + (4 - i) * 10, DockTheme.DragYellow), 2.2f * s);
                    g.DrawPath(pen, gp);
                }

                using (var path = RoundRect(r, radius))
                {
                    using var fill = new LinearGradientBrush(r, Color.FromArgb(30, 29, 24), DockTheme.DragFill, LinearGradientMode.Vertical);
                    g.FillPath(fill, path);
                    using var edge = new Pen(DockTheme.DragYellow, 2f * s);
                    g.DrawPath(edge, path);
                }
            }
            else
            {
                using var path = RoundRect(r, radius);
                using var fill = new SolidBrush(DockTheme.Surface);
                g.FillPath(fill, path);
                using var dash = new Pen(DockTheme.DashedBorder, Math.Max(1f, 1.2f * s)) { DashPattern = new[] { 4f, 3f } };
                g.DrawPath(dash, path);
            }

            if (f.DragActive) PaintDragContent(g, r, s, f.DragCounts);
            else if (f.Flash != null) PaintFlashContent(g, r, s, f.Flash);
            else PaintIdleContent(g, r, s);
        }

        private static void PaintIdleContent(Graphics g, Rectangle r, float s)
        {
            float tile = 48 * s;
            float top = r.Top + (r.Height - (tile + 52 * s)) / 2f;
            var t = new RectangleF(r.Left + (r.Width - tile) / 2f, top, tile, tile);
            PaintBoltTile(g, t, s, yellow: false);

            using var font = DockTheme.Font(10f, s);
            using var small = DockTheme.Font(8.25f, s);
            DrawCentered(g, "Drop media to render", font, DockTheme.Muted, r, t.Bottom + 12 * s);
            DrawCentered(g, "Images, videos, audio or folders", small, DockTheme.Subtle, r, t.Bottom + 32 * s);
        }

        private static void PaintDragContent(Graphics g, Rectangle r, float s, MediaCounts? counts)
        {
            float tile = 44 * s;
            float block = tile + 96 * s;
            float top = r.Top + (r.Height - block) / 2f;
            var t = new RectangleF(r.Left + (r.Width - tile) / 2f, top, tile, tile);
            PaintBoltTile(g, t, s, yellow: true);

            using var title = DockTheme.Font(11f, s, FontStyle.Bold);
            DrawCentered(g, "Release to add to Render Dock", title, DockTheme.Text, r, t.Bottom + 10 * s);

            float chipsY = t.Bottom + 40 * s;
            if (counts is { Total: > 0 } c)
            {
                PaintCountChips(g, r, s, chipsY, c, DockTheme.DragYellow);
                using var note = DockTheme.Font(8f, s);
                DrawCentered(g, "Job type is confirmed when you drop", note, DockTheme.Subtle, r, chipsY + 30 * s);
            }
            else
            {
                using var note = DockTheme.Font(8.75f, s);
                DrawCentered(g, counts == null ? "Files will be checked when you drop" : "No supported images, videos or audio detected", note, DockTheme.Muted, r, chipsY + 4 * s);
            }
        }

        private static void PaintFlashContent(Graphics g, Rectangle r, float s, DropSummary flash)
        {
            float badge = 30 * s;
            float block = badge + 90 * s;
            float top = r.Top + (r.Height - block) / 2f;
            var b = new RectangleF(r.Left + (r.Width - badge) / 2f, top, badge, badge);
            Color tone = flash.Added ? DockTheme.Success : DockTheme.Muted;
            using (var path = RoundRect(Rectangle.Round(b), 8 * s))
            using (var fill = new SolidBrush(Color.FromArgb(38, tone)))
            {
                g.FillPath(fill, path);
            }

            if (flash.Added) DrawCheck(g, b, s, tone);
            else DrawDash(g, b, s, tone);

            using var title = DockTheme.Font(10.5f, s, FontStyle.Bold);
            DrawCentered(g, flash.Headline, title, DockTheme.Text, r, b.Bottom + 10 * s);

            float y = b.Bottom + 34 * s;
            if (flash.Counts is { Total: > 0 } c)
            {
                PaintCountChips(g, r, s, y, c, DockTheme.AccentStart);
                y += 30 * s;
            }

            if (!string.IsNullOrEmpty(flash.Detail))
            {
                using var detail = DockTheme.Font(8.5f, s);
                DrawCentered(g, flash.Detail!, detail, DockTheme.Muted, Rectangle.Inflate(r, -(int)(14 * s), 0), y);
            }
        }

        private static void PaintCountChips(Graphics g, Rectangle r, float s, float y, MediaCounts c, Color numberColor)
        {
            var chips = new List<(string Number, string Label)>();
            if (c.Images > 0) chips.Add((c.Images.ToString(), c.Images == 1 ? "image" : "images"));
            if (c.Videos > 0) chips.Add((c.Videos.ToString(), c.Videos == 1 ? "video" : "videos"));
            if (c.Audio > 0) chips.Add((c.Audio.ToString(), "audio"));

            using var num = DockTheme.Font(8.75f, s, FontStyle.Bold);
            using var lbl = DockTheme.Font(8.75f, s);
            var fmt = StringFormat.GenericTypographic;
            float padX = 10 * s, gap = 6 * s, h = 24 * s, space = 4 * s;

            var widths = new List<float>();
            float total = 0;
            foreach (var chip in chips)
            {
                float w = g.MeasureString(chip.Number, num, PointF.Empty, fmt).Width + space
                          + g.MeasureString(chip.Label, lbl, PointF.Empty, fmt).Width + 2 * padX;
                widths.Add(w);
                total += w;
            }

            total += gap * (chips.Count - 1);
            float x = r.Left + (r.Width - total) / 2f;
            using var numBrush = new SolidBrush(numberColor);
            using var lblBrush = new SolidBrush(DockTheme.Text);
            using var fill = new SolidBrush(Color.FromArgb(140, DockTheme.Card));
            using var border = new Pen(DockTheme.CardBorder, Math.Max(1f, s));
            for (int i = 0; i < chips.Count; i++)
            {
                var rect = new RectangleF(x, y, widths[i], h);
                using (var path = RoundRect(Rectangle.Round(rect), h / 2f))
                {
                    g.FillPath(fill, path);
                    g.DrawPath(border, path);
                }

                float nh = g.MeasureString(chips[i].Number, num, PointF.Empty, fmt).Height;
                float tx = x + padX;
                g.DrawString(chips[i].Number, num, numBrush, tx, y + (h - nh) / 2f, fmt);
                tx += g.MeasureString(chips[i].Number, num, PointF.Empty, fmt).Width + space;
                g.DrawString(chips[i].Label, lbl, lblBrush, tx, y + (h - nh) / 2f, fmt);
                x += widths[i] + gap;
            }
        }

        // ----------------------------------------------------------------- queue

        private static void PaintQueue(Graphics g, DockLayout l, float s, DockFrame f)
        {
            using (var label = DockTheme.Font(7.5f, s, FontStyle.Bold))
            using (var brush = new SolidBrush(DockTheme.Subtle))
            {
                int active = 0, queued = 0;
                foreach (var job in f.Jobs)
                {
                    if (job.State == DockJobState.Rendering) active++;
                    else if (job.State == DockJobState.Queued) queued++;
                }

                g.DrawString("RENDER QUEUE", label, brush, l.QueueLabel.Left, l.QueueLabel.Top, StringFormat.GenericTypographic);
                string tally = active + queued == 0 ? string.Empty : $"{active} rendering · {queued} queued";
                if (tally.Length > 0)
                {
                    using var right = new StringFormat(StringFormat.GenericTypographic) { Alignment = StringAlignment.Far };
                    using var tallyFont = DockTheme.Font(7.75f, s);
                    g.DrawString(tally, tallyFont, brush, new RectangleF(l.QueueLabel.Left, l.QueueLabel.Top, l.QueueLabel.Width, l.QueueLabel.Height), right);
                }
            }

            if (f.Jobs.Count == 0)
            {
                using var empty = DockTheme.Font(8.75f, s);
                using var brush = new SolidBrush(DockTheme.Subtle);
                g.DrawString("No renders yet. Dropped media appears here.", empty, brush, l.QueueArea.Left, l.QueueArea.Top + 4 * s, StringFormat.GenericTypographic);
                return;
            }

            for (int i = 0; i < l.Cards.Count && i < f.Jobs.Count; i++)
            {
                PaintJobCard(g, l.Cards[i], s, f.Jobs[i], f.Phase);
            }

            if (l.HiddenJobs > 0)
            {
                using var more = DockTheme.Font(8f, s);
                using var brush = new SolidBrush(DockTheme.Muted);
                using var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString($"+{l.HiddenJobs} more", more, brush, l.MoreLabel, center);
            }
        }

        private static void PaintJobCard(Graphics g, Rectangle r, float s, DockJobEntry job, float phase)
        {
            using (var path = RoundRect(r, 10 * s))
            {
                using var fill = new SolidBrush(DockTheme.Card);
                g.FillPath(fill, path);
                using var border = new Pen(job.State == DockJobState.Failed ? Color.FromArgb(90, DockTheme.Failure) : DockTheme.CardBorder, Math.Max(1f, s));
                g.DrawPath(border, path);
            }

            float px = 14 * s;
            (string status, Color tone) = job.State switch
            {
                DockJobState.Rendering => ("Rendering", DockTheme.AccentStart),
                DockJobState.Queued => ("Queued", DockTheme.Muted),
                DockJobState.Completed => ("Complete", DockTheme.Success),
                _ => ("Failed", DockTheme.Failure),
            };

            using var statusFont = DockTheme.Font(8.25f, s, job.State == DockJobState.Queued ? FontStyle.Regular : FontStyle.Bold);
            float statusW = g.MeasureString(status, statusFont, PointF.Empty, StringFormat.GenericTypographic).Width;
            float statusX = r.Right - px - statusW;
            using (var statusBrush = new SolidBrush(tone))
            {
                g.DrawString(status, statusFont, statusBrush, statusX, r.Top + 11 * s, StringFormat.GenericTypographic);
            }

            using var nameFont = DockTheme.Font(9.5f, s, FontStyle.Bold);
            using var kindFont = DockTheme.Font(8f, s);
            float textW = statusX - (r.Left + px) - 10 * s;
            using (var nameBrush = new SolidBrush(DockTheme.Text))
            {
                DrawFitted(g, job.Name, nameFont, nameBrush, new RectangleF(r.Left + px, r.Top + 10 * s, textW, 18 * s));
            }

            string sub = job.State == DockJobState.Failed && !string.IsNullOrEmpty(job.FailureReason) ? job.FailureReason! : job.KindLabel;
            using (var kindBrush = new SolidBrush(job.State == DockJobState.Failed ? Color.FromArgb(220, DockTheme.Failure) : DockTheme.Muted))
            {
                DrawFitted(g, sub, kindFont, kindBrush, new RectangleF(r.Left + px, r.Top + 29 * s, r.Width - 2 * px, 16 * s));
            }

            var track = new RectangleF(r.Left + px, r.Bottom - 12 * s, r.Width - 2 * px, 4 * s);
            using (var trackPath = RoundRect(track, 2 * s))
            using (var trackBrush = new SolidBrush(DockTheme.Track))
            {
                g.FillPath(trackBrush, trackPath);
            }

            switch (job.State)
            {
                case DockJobState.Rendering:
                    // The runner exposes no percentage, so this is an honest
                    // indeterminate sweep — not a fabricated progress value.
                    float seg = track.Width * 0.32f;
                    float travel = track.Width + seg;
                    float x = track.Left - seg + travel * phase;
                    var bar = RectangleF.FromLTRB(Math.Max(track.Left, x), track.Top, Math.Min(track.Right, x + seg), track.Bottom);
                    if (bar.Width > 1)
                    {
                        var state = g.Save();
                        using var clip = RoundRect(track, 2 * s);
                        g.SetClip(clip);
                        using var brush = new LinearGradientBrush(new RectangleF(x, track.Top, seg, track.Height), DockTheme.AccentStart, DockTheme.AccentEnd, LinearGradientMode.Horizontal);
                        g.FillRectangle(brush, bar);
                        g.Restore(state);
                    }

                    break;
                case DockJobState.Completed:
                    using (var p = RoundRect(track, 2 * s))
                    using (var b = new SolidBrush(Color.FromArgb(170, DockTheme.Success))) g.FillPath(b, p);
                    break;
                case DockJobState.Failed:
                    using (var p = RoundRect(track, 2 * s))
                    using (var b = new SolidBrush(Color.FromArgb(170, DockTheme.Failure))) g.FillPath(b, p);
                    break;
            }
        }

        // ---------------------------------------------------------------- notice

        private static void PaintNotice(Graphics g, DockLayout l, float s, DockJobEntry job)
        {
            var r = l.Notice;
            bool ok = job.State == DockJobState.Completed;
            Color tone = ok ? DockTheme.Success : DockTheme.Failure;

            using (var path = RoundRect(r, 12 * s))
            {
                using var fill = new SolidBrush(DockTheme.Card);
                g.FillPath(fill, path);
                using var border = new Pen(ok ? DockTheme.CardBorder : Color.FromArgb(110, DockTheme.Failure), Math.Max(1f, s));
                g.DrawPath(border, path);
            }

            var badge = new RectangleF(r.Left + 16 * s, r.Top + 16 * s, 36 * s, 36 * s);
            using (var bp = RoundRect(Rectangle.Round(badge), 9 * s))
            using (var bf = new SolidBrush(Color.FromArgb(34, tone)))
            {
                g.FillPath(bf, bp);
            }

            if (ok) DrawCheck(g, badge, s, tone);
            else DrawBang(g, badge, s, tone);

            float tx = r.Left + 64 * s;
            float tw = l.DismissButton.Left - tx - 6 * s;
            using var title = DockTheme.Font(10f, s, FontStyle.Bold);
            using var sub = DockTheme.Font(8.25f, s);
            using (var b = new SolidBrush(DockTheme.Text))
            {
                DrawFitted(g, ok ? "Render complete" : "Render failed", title, b, new RectangleF(tx, r.Top + 15 * s, tw, 20 * s));
            }

            string detail = ok ? $"{job.Name} · {job.KindLabel}" : job.Name;
            using (var b = new SolidBrush(DockTheme.Muted))
            {
                DrawFitted(g, detail, sub, b, new RectangleF(tx, r.Top + 36 * s, r.Right - tx - 16 * s, 16 * s));
            }

            if (!ok)
            {
                using var b = new SolidBrush(Color.FromArgb(230, DockTheme.Failure));
                using var wrap = new StringFormat(StringFormat.GenericTypographic) { Trimming = StringTrimming.EllipsisCharacter };
                g.DrawString(job.FailureReason ?? "The render did not finish.", sub, b, new RectangleF(tx, r.Top + 56 * s, r.Right - tx - 16 * s, 34 * s), wrap);
            }
            else if (job.OutputPath == null)
            {
                using var b = new SolidBrush(DockTheme.Subtle);
                using var note = DockTheme.Font(7.75f, s);
                DrawFitted(g, "File not located; opens folder", note, b, new RectangleF(l.FolderButton.Right + 8 * s, l.FolderButton.Top + 8 * s, r.Right - l.FolderButton.Right - 20 * s, 16 * s));
            }
        }

        private static void PaintNoticeButtons(Graphics g, DockLayout l, float s, DockJobEntry job)
        {
            PaintIconButton(g, l.DismissButton, s, false, false, Glyph.Close);
            if (job.State == DockJobState.Completed)
            {
                PaintButton(g, l.PlayButton, s, "Play", DockButtonKind.Primary, false, false, job.OutputPath != null);
                PaintButton(g, l.FolderButton, s, "Show in folder", DockButtonKind.Secondary, false, false, true);
            }
        }

        // --------------------------------------------------------------- buttons

        internal enum Glyph { Folder, Close }

        public static void PaintButton(Graphics g, Rectangle r, float s, string text, DockButtonKind kind, bool hover, bool focused, bool enabled)
        {
            Prepare(g);
            var rect = new Rectangle(r.X, r.Y, r.Width - 1, r.Height - 1);
            using var path = RoundRect(rect, 7 * s);
            if (kind == DockButtonKind.Primary)
            {
                var a = enabled ? DockTheme.AccentStart : Color.FromArgb(70, 90, 110);
                var b = enabled ? DockTheme.AccentEnd : Color.FromArgb(55, 70, 92);
                if (hover && enabled) { a = ControlPaintLight(a); b = ControlPaintLight(b); }
                using var fill = new LinearGradientBrush(rect, a, b, LinearGradientMode.Horizontal);
                g.FillPath(fill, path);
            }
            else
            {
                using var fill = new SolidBrush(hover ? Color.FromArgb(36, 49, 74) : Color.FromArgb(27, 38, 58));
                g.FillPath(fill, path);
                using var border = new Pen(Color.FromArgb(48, 63, 92), Math.Max(1f, s));
                g.DrawPath(border, path);
            }

            if (focused)
            {
                using var ring = new Pen(Color.FromArgb(200, DockTheme.AccentStart), 1.5f * s);
                using var focusPath = RoundRect(Rectangle.Inflate(rect, -(int)(2 * s), -(int)(2 * s)), 6 * s);
                g.DrawPath(ring, focusPath);
            }

            using var font = DockTheme.Font(8.75f, s, FontStyle.Bold);
            Color textColor = kind == DockButtonKind.Primary ? Color.FromArgb(8, 18, 32) : DockTheme.Text;
            if (!enabled) textColor = Color.FromArgb(150, DockTheme.Muted);
            using var brush = new SolidBrush(textColor);
            using var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(text, font, brush, rect, center);
        }

        public static void PaintIconButton(Graphics g, Rectangle r, float s, bool hover, bool focused, Glyph glyph)
        {
            Prepare(g);
            var rect = new Rectangle(r.X, r.Y, r.Width - 1, r.Height - 1);
            if (hover || focused)
            {
                using var path = RoundRect(rect, 7 * s);
                using var fill = new SolidBrush(Color.FromArgb(30, 42, 64));
                g.FillPath(fill, path);
                if (focused)
                {
                    using var ring = new Pen(Color.FromArgb(200, DockTheme.AccentStart), 1.5f * s);
                    g.DrawPath(ring, path);
                }
            }

            using var pen = new Pen(hover ? DockTheme.Text : DockTheme.Muted, 1.4f * s) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
            float cx = rect.Left + rect.Width / 2f, cy = rect.Top + rect.Height / 2f;
            if (glyph == Glyph.Close)
            {
                float d = 4.5f * s;
                g.DrawLine(pen, cx - d, cy - d, cx + d, cy + d);
                g.DrawLine(pen, cx + d, cy - d, cx - d, cy + d);
            }
            else
            {
                float w = 14 * s, h = 10.5f * s;
                float x = cx - w / 2f, y = cy - h / 2f + 0.5f * s;
                using var folder = new GraphicsPath();
                folder.AddLines(new[]
                {
                    new PointF(x, y + h), new PointF(x, y), new PointF(x + w * 0.38f, y),
                    new PointF(x + w * 0.5f, y + 2.2f * s), new PointF(x + w, y + 2.2f * s), new PointF(x + w, y + h),
                });
                folder.CloseFigure();
                g.DrawPath(pen, folder);
            }
        }

        // --------------------------------------------------------------- helpers

        private static Color ControlPaintLight(Color c) =>
            Color.FromArgb(c.A, Math.Min(255, c.R + 18), Math.Min(255, c.G + 18), Math.Min(255, c.B + 18));

        private static void PaintBoltTile(Graphics g, RectangleF t, float s, bool yellow)
        {
            using var path = RoundRect(Rectangle.Round(t), 12 * s);
            Color a = yellow ? DockTheme.DragYellow : DockTheme.AccentStart;
            Color b = yellow ? DockTheme.DragYellowDeep : DockTheme.AccentEnd;
            using (var fill = new LinearGradientBrush(t, a, b, 60f))
            {
                g.FillPath(fill, path);
            }

            var bolt = new RectangleF(t.Left + t.Width * 0.33f, t.Top + t.Height * 0.24f, t.Width * 0.34f, t.Height * 0.52f);
            using var boltBrush = new SolidBrush(yellow ? Color.FromArgb(32, 26, 6) : Color.White);
            FillBolt(g, boltBrush, bolt);
        }

        private static void FillBolt(Graphics g, Brush brush, RectangleF r)
        {
            PointF P(float x, float y) => new(r.Left + r.Width * x, r.Top + r.Height * y);
            using var bolt = new GraphicsPath();
            bolt.AddPolygon(new[]
            {
                P(0.62f, 0f), P(0.04f, 0.58f), P(0.46f, 0.58f), P(0.34f, 1f), P(0.96f, 0.40f), P(0.54f, 0.40f),
            });
            g.FillPath(brush, bolt);
        }

        private static void DrawCheck(Graphics g, RectangleF b, float s, Color tone)
        {
            using var pen = new Pen(tone, 2.2f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            float cx = b.Left + b.Width / 2f, cy = b.Top + b.Height / 2f;
            g.DrawLines(pen, new[]
            {
                new PointF(cx - 5.5f * s, cy + 0.5f * s), new PointF(cx - 1.5f * s, cy + 4.5f * s), new PointF(cx + 6f * s, cy - 4f * s),
            });
        }

        private static void DrawBang(Graphics g, RectangleF b, float s, Color tone)
        {
            using var pen = new Pen(tone, 2.4f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            float cx = b.Left + b.Width / 2f, cy = b.Top + b.Height / 2f;
            g.DrawLine(pen, cx, cy - 6f * s, cx, cy + 1.5f * s);
            using var dot = new SolidBrush(tone);
            g.FillEllipse(dot, cx - 1.5f * s, cy + 4.5f * s, 3f * s, 3f * s);
        }

        private static void DrawDash(Graphics g, RectangleF b, float s, Color tone)
        {
            using var pen = new Pen(tone, 2.2f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            float cx = b.Left + b.Width / 2f, cy = b.Top + b.Height / 2f;
            g.DrawLine(pen, cx - 5 * s, cy, cx + 5 * s, cy);
        }

        private static void DrawCentered(Graphics g, string text, Font font, Color color, Rectangle bounds, float y)
        {
            using var brush = new SolidBrush(color);
            using var fmt = new StringFormat(StringFormat.GenericTypographic) { Alignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap };
            float width = bounds.Width - 16;
            string fitted = Fit(g, text, font, width);
            g.DrawString(fitted, font, brush, new RectangleF(bounds.Left + 8, y, width, font.GetHeight(g) + 2), fmt);
        }

        /// <summary>Draws one line, trimming with an ellipsis so text never clips mid-glyph.</summary>
        private static void DrawFitted(Graphics g, string text, Font font, Brush brush, RectangleF r)
        {
            string fitted = Fit(g, text, font, r.Width);
            g.DrawString(fitted, font, brush, r.Left, r.Top, StringFormat.GenericTypographic);
        }

        internal static string Fit(Graphics g, string text, Font font, float width)
        {
            if (string.IsNullOrEmpty(text) || width <= 0) return string.Empty;
            var fmt = StringFormat.GenericTypographic;
            if (g.MeasureString(text, font, PointF.Empty, fmt).Width <= width) return text;
            int lo = 0, hi = text.Length;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                string candidate = text.Substring(0, mid).TrimEnd() + "…";
                if (g.MeasureString(candidate, font, PointF.Empty, fmt).Width <= width) lo = mid;
                else hi = mid - 1;
            }

            return lo == 0 ? "…" : text.Substring(0, lo).TrimEnd() + "…";
        }

        internal static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            float d = Math.Max(0.5f, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
            var path = new GraphicsPath();
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
