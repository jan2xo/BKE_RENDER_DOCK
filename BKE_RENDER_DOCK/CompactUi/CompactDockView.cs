using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace BKE_MediaTools.CompactUi
{
    /// <summary>
    /// The compact Render Dock surface. Presentation only: it paints the drop
    /// zone, queue cards and completion notice from <see cref="DockJobFeed"/>,
    /// and raises events. The host form owns every action.
    /// </summary>
    internal sealed class CompactDockView : Control
    {
        private static readonly TimeSpan FlashDuration = TimeSpan.FromSeconds(4.5);

        private readonly DockJobFeed _feed;
        private readonly DockButton _outputs;
        private readonly DockButton _play;
        private readonly DockButton _folder;
        private readonly DockButton _dismiss;
        private readonly System.Windows.Forms.Timer _animation = new() { Interval = 33 };
        private readonly System.Windows.Forms.Timer _flashTimer = new();
        private DockLayout _layout = new();
        private float _phase;
        private bool _dragActive;
        private MediaCounts? _dragCounts;
        private DropSummary? _flash;

        public CompactDockView(DockJobFeed feed)
        {
            _feed = feed;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = DockTheme.Window;
            ForeColor = DockTheme.Text;
            AllowDrop = true;
            TabStop = false;
            AccessibleRole = AccessibleRole.Pane;
            AccessibleName = "Render Dock";

            _outputs = new DockButton(DockButtonKind.Icon, "Open output folder") { Glyph = DockPainter.Glyph.Folder };
            _play = new DockButton(DockButtonKind.Primary, "Play");
            _folder = new DockButton(DockButtonKind.Secondary, "Show in folder");
            _dismiss = new DockButton(DockButtonKind.Icon, "Dismiss notification") { Glyph = DockPainter.Glyph.Close };

            _outputs.Click += (_, __) => OpenOutputsRequested?.Invoke(this, EventArgs.Empty);
            _play.Click += (_, __) => { if (_feed.Notice is { } n) PlayRequested?.Invoke(this, n); };
            _folder.Click += (_, __) => { if (_feed.Notice is { } n) ShowInFolderRequested?.Invoke(this, n); };
            _dismiss.Click += (_, __) => _feed.DismissNotice();

            new ToolTip().SetToolTip(_outputs, "Open output folder");
            Controls.AddRange(new Control[] { _outputs, _play, _folder, _dismiss });

            _animation.Tick += (_, __) =>
            {
                _phase = (_phase + 0.012f) % 1f;
                InvalidateCards();
            };
            _flashTimer.Tick += (_, __) => { _flashTimer.Stop(); _flash = null; Invalidate(); };

            _feed.Changed += (_, __) => SyncFromFeed();
            SyncFromFeed();
        }

        public event EventHandler? OpenOutputsRequested;
        public event EventHandler<DockJobEntry>? PlayRequested;
        public event EventHandler<DockJobEntry>? ShowInFolderRequested;

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public MediaCounts? DragCounts => _dragCounts;

        public void SetDragState(bool active, MediaCounts? counts)
        {
            _dragActive = active;
            _dragCounts = active ? counts : null;
            if (active)
            {
                _flash = null;
                _flashTimer.Stop();
                AccessibleDescription = counts is { Total: > 0 } c
                    ? $"Release to add to Render Dock. {c.Format()}."
                    : "Release to add to Render Dock.";
            }

            Invalidate();
        }

        public void ShowDropSummary(DropSummary summary)
        {
            _flash = summary;
            AccessibleDescription = summary.Detail == null ? summary.Headline : $"{summary.Headline}. {summary.Detail}";
            _flashTimer.Stop();
            _flashTimer.Interval = (int)FlashDuration.TotalMilliseconds;
            _flashTimer.Start();
            Invalidate();
        }

        private float DpiScale => DeviceDpi / 96f;

        private void SyncFromFeed()
        {
            bool rendering = _feed.HasActiveWork;
            if (rendering && !_animation.Enabled) _animation.Start();
            if (!rendering && _animation.Enabled) _animation.Stop();
            RelayoutChildren();
            Invalidate();
        }

        private void RelayoutChildren()
        {
            var jobs = _feed.Ordered();
            var notice = _feed.Notice;
            _layout = DockLayout.Compute(ClientSize, DpiScale, jobs.Count, notice != null);

            _outputs.Bounds = _layout.OutputsButton;
            bool completed = notice?.State == DockJobState.Completed;
            _dismiss.Visible = notice != null;
            _play.Visible = completed;
            _folder.Visible = completed;
            if (notice != null)
            {
                _dismiss.Bounds = _layout.DismissButton;
                _play.Bounds = _layout.PlayButton;
                _folder.Bounds = _layout.FolderButton;
                _play.Enabled = completed && notice.OutputPath != null;
            }
        }

        private void InvalidateCards()
        {
            foreach (var card in _layout.Cards) Invalidate(card);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            RelayoutChildren();
        }

        protected override void OnDpiChangedAfterParent(EventArgs e)
        {
            base.OnDpiChangedAfterParent(e);
            RelayoutChildren();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var frame = new DockFrame
            {
                DragActive = _dragActive,
                DragCounts = _dragCounts,
                Flash = _flash,
                Jobs = _feed.Ordered(),
                Notice = _feed.Notice,
                Phase = _phase,
            };

            DockPainter.Paint(e.Graphics, ClientSize, DpiScale, frame, _layout, paintButtons: false);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _animation.Dispose();
                _flashTimer.Dispose();
            }

            base.Dispose(disposing);
        }

        /// <summary>Keyboard-focusable button painted with the dock's own style.</summary>
        private sealed class DockButton : Button
        {
            private readonly DockButtonKind _kind;
            private bool _hover;

            public DockButton(DockButtonKind kind, string text)
            {
                _kind = kind;
                Text = text;
                AccessibleName = text;
                FlatStyle = FlatStyle.Flat;
                FlatAppearance.BorderSize = 0;
                UseVisualStyleBackColor = false;
                BackColor = kind == DockButtonKind.Icon ? DockTheme.HeaderFill : DockTheme.Card;
                Cursor = Cursors.Hand;
                TabStop = true;
                SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer, true);
            }

            [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
            public DockPainter.Glyph Glyph { get; init; }

            protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Cursor = Enabled ? Cursors.Hand : Cursors.Default; }

            protected override void OnPaint(PaintEventArgs e)
            {
                float s = DeviceDpi / 96f;
                var parentFill = Parent is CompactDockView && Glyph == DockPainter.Glyph.Folder && _kind == DockButtonKind.Icon
                    ? DockTheme.HeaderFill
                    : DockTheme.Card;
                e.Graphics.Clear(parentFill);
                bool focused = Focused && ShowFocusCues;
                if (_kind == DockButtonKind.Icon)
                {
                    DockPainter.PaintIconButton(e.Graphics, ClientRectangle, s, _hover, focused, Glyph);
                }
                else
                {
                    DockPainter.PaintButton(e.Graphics, ClientRectangle, s, Text, _kind, _hover, focused, Enabled);
                }
            }
        }
    }
}
