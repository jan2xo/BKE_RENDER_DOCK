using BKE.Notifications;
using BKE_MediaTools.Licensing;
using System.Diagnostics;
using System.Reflection;

namespace BKE_MediaTools.Notifications
{
    internal static class NotificationTrayController
    {
        private const string ProductId = "bke-render-dock";

        internal static void Attach(Form form, bool enterpriseSession)
        {
            ArgumentNullException.ThrowIfNull(form);
            _ = new Binding(form, enterpriseSession);
        }

        private sealed class Binding : IDisposable
        {
            private readonly Form _owner;
            private readonly bool _enterpriseSession;
            private readonly Button _bell;
            private readonly ToolTip _toolTip = new();
            private bool _refreshing;
            private bool _disposed;

            internal Binding(Form owner, bool enterpriseSession)
            {
                _owner = owner;
                _enterpriseSession = enterpriseSession;
                _bell = new Button
                {
                    Name = "notificationBell",
                    Text = "🔔",
                    AccessibleName = "Notifications",
                    AccessibleDescription = "Open Render Dock notifications",
                    Width = 52,
                    Height = 34,
                    Top = 10,
                    Left = Math.Max(10, owner.ClientSize.Width - 62),
                    Anchor = AnchorStyles.Top | AnchorStyles.Right,
                    TabStop = false,
                    UseVisualStyleBackColor = true
                };

                _toolTip.SetToolTip(_bell, "Notifications");
                _bell.Click += Bell_Click;
                owner.Controls.Add(_bell);
                _bell.BringToFront();

                owner.Shown += Owner_Shown;
                owner.Activated += Owner_Activated;
                owner.FormClosed += Owner_FormClosed;
            }

            private async void Owner_Shown(object? sender, EventArgs e)
            {
                await RefreshBadgeAsync().ConfigureAwait(true);
            }

            private async void Owner_Activated(object? sender, EventArgs e)
            {
                await RefreshBadgeAsync().ConfigureAwait(true);
            }

            private async void Bell_Click(object? sender, EventArgs e)
            {
                await ShowTrayAsync().ConfigureAwait(true);
            }

            private void Owner_FormClosed(object? sender, FormClosedEventArgs e)
            {
                Dispose();
            }

            private async Task RefreshBadgeAsync()
            {
                if (_disposed || _refreshing || _owner.IsDisposed || _bell.IsDisposed)
                {
                    return;
                }

                _refreshing = true;
                try
                {
                    using var client = CreateClient();
                    int unread;

                    if (_enterpriseSession)
                    {
                        var feed = await client.GetFeedAsync(
                            new NotificationFeedQuery(limit: 50, includeDismissed: false)).ConfigureAwait(true);

                        if (!feed.Succeeded)
                        {
                            LogFailure("badge feed", feed.Error);
                            return;
                        }

                        unread = feed.Items.Count(item =>
                            item.State == NotificationState.Unread &&
                            item.Category != NotificationCategory.Licensing);
                    }
                    else
                    {
                        var count = await client.GetUnreadCountAsync().ConfigureAwait(true);
                        if (!count.Succeeded)
                        {
                            LogFailure("unread count", count.Error);
                            return;
                        }

                        unread = count.Count;
                    }

                    _bell.Text = unread > 0 ? $"🔔 {unread}" : "🔔";
                    _toolTip.SetToolTip(
                        _bell,
                        unread > 0 ? $"Notifications — {unread} unread" : "Notifications");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Render Dock notification tray badge failed: {ex.Message}");
                }
                finally
                {
                    _refreshing = false;
                }
            }

            private async Task ShowTrayAsync()
            {
                if (_disposed || _owner.IsDisposed)
                {
                    return;
                }

                using var tray = new Form
                {
                    Text = "Render Dock Notifications",
                    Width = 440,
                    Height = 500,
                    MinimumSize = new Size(400, 360),
                    StartPosition = FormStartPosition.CenterParent,
                    ShowInTaskbar = false
                };

                var root = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    ColumnCount = 1,
                    RowCount = 2,
                    Padding = new Padding(10)
                };
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

                var header = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    AutoSize = true,
                    FlowDirection = FlowDirection.LeftToRight,
                    WrapContents = false
                };
                var refresh = new Button { Text = "Refresh", AutoSize = true };
                var status = new Label
                {
                    AutoSize = true,
                    Padding = new Padding(8, 8, 0, 0),
                    Text = "Loading…"
                };
                header.Controls.Add(refresh);
                header.Controls.Add(status);

                var feedPanel = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    AutoScroll = true,
                    FlowDirection = FlowDirection.TopDown,
                    WrapContents = false,
                    Padding = new Padding(0, 8, 0, 0)
                };

                root.Controls.Add(header, 0, 0);
                root.Controls.Add(feedPanel, 0, 1);
                tray.Controls.Add(root);

                async Task LoadAsync()
                {
                    refresh.Enabled = false;
                    feedPanel.Controls.Clear();
                    status.Text = "Loading…";

                    try
                    {
                        using var client = CreateClient();
                        var feed = await client.GetFeedAsync(
                            new NotificationFeedQuery(limit: 50, includeDismissed: false)).ConfigureAwait(true);

                        if (!feed.Succeeded)
                        {
                            status.Text = "Notifications are temporarily unavailable.";
                            LogFailure("feed", feed.Error);
                            return;
                        }

                        var visibleItems = feed.Items
                            .Where(item => !_enterpriseSession || item.Category != NotificationCategory.Licensing)
                            .OrderByDescending(item => item.CreatedAt)
                            .ToArray();

                        status.Text = visibleItems.Length == 0
                            ? "No notifications."
                            : $"{visibleItems.Length} notification{(visibleItems.Length == 1 ? string.Empty : "s")}";

                        foreach (var item in visibleItems)
                        {
                            feedPanel.Controls.Add(CreateItemPanel(tray, item, LoadAsync));
                        }
                    }
                    catch (Exception ex)
                    {
                        status.Text = "Notifications are temporarily unavailable.";
                        Debug.WriteLine($"Render Dock notification tray feed failed: {ex.Message}");
                    }
                    finally
                    {
                        refresh.Enabled = true;
                        await RefreshBadgeAsync().ConfigureAwait(true);
                    }
                }

                refresh.Click += async (_, __) => await LoadAsync().ConfigureAwait(true);
                tray.Shown += async (_, __) => await LoadAsync().ConfigureAwait(true);
                tray.ShowDialog(_owner);
            }

            private Panel CreateItemPanel(Form tray, NotificationItem item, Func<Task> reload)
            {
                var panel = new Panel
                {
                    Width = 382,
                    Height = 156,
                    BorderStyle = BorderStyle.FixedSingle,
                    Margin = new Padding(0, 0, 0, 8),
                    Padding = new Padding(8)
                };

                var title = new Label
                {
                    AutoEllipsis = true,
                    Text = item.State == NotificationState.Unread ? $"● {item.Title}" : item.Title,
                    Font = new Font(_owner.Font, item.State == NotificationState.Unread ? FontStyle.Bold : FontStyle.Regular),
                    Left = 8,
                    Top = 8,
                    Width = 350,
                    Height = 22
                };

                var metadata = new Label
                {
                    AutoEllipsis = true,
                    Text = $"{item.Category} • {item.CreatedAt.LocalDateTime:g}",
                    Left = 8,
                    Top = 32,
                    Width = 350,
                    Height = 20
                };

                var body = new Label
                {
                    AutoEllipsis = true,
                    Text = item.Body,
                    Left = 8,
                    Top = 56,
                    Width = 350,
                    Height = 48
                };

                var open = new Button
                {
                    Text = item.State == NotificationState.Unread ? "Open / mark read" : "Open",
                    AutoSize = true,
                    Left = 8,
                    Top = 112
                };

                var dismiss = new Button
                {
                    Text = "Dismiss",
                    AutoSize = true,
                    Left = 148,
                    Top = 112
                };

                open.Click += async (_, __) =>
                {
                    open.Enabled = false;
                    try
                    {
                        if (item.State == NotificationState.Unread)
                        {
                            using var client = CreateClient();
                            var result = await client.MarkReadAsync(item.Id).ConfigureAwait(true);
                            if (result.Status != NotificationOperationStatus.Succeeded)
                            {
                                LogFailure("mark read", result.Error);
                            }
                        }

                        MessageBox.Show(
                            tray,
                            item.Body,
                            item.Title,
                            MessageBoxButtons.OK,
                            IconFor(item.Severity));
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Render Dock notification open failed: {ex.Message}");
                    }
                    finally
                    {
                        if (!tray.IsDisposed)
                        {
                            await reload().ConfigureAwait(true);
                        }
                    }
                };

                dismiss.Click += async (_, __) =>
                {
                    dismiss.Enabled = false;
                    try
                    {
                        using var client = CreateClient();
                        var result = await client.DismissAsync(item.Id).ConfigureAwait(true);
                        if (result.Status != NotificationOperationStatus.Succeeded)
                        {
                            LogFailure("dismiss", result.Error);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Render Dock notification dismiss failed: {ex.Message}");
                    }
                    finally
                    {
                        if (!tray.IsDisposed)
                        {
                            await reload().ConfigureAwait(true);
                        }
                    }
                };

                panel.Controls.Add(title);
                panel.Controls.Add(metadata);
                panel.Controls.Add(body);
                panel.Controls.Add(open);
                panel.Controls.Add(dismiss);
                return panel;
            }

            private static BkeNotificationInboxClient CreateClient() =>
                BkeNotificationInboxClient.Create(
                    ProductId,
                    CurrentVersion(),
                    InstallationIdentity.GetOrCreate());

            private static string CurrentVersion()
            {
                var version = Assembly.GetExecutingAssembly().GetName().Version;
                return version == null
                    ? "0.0.0"
                    : $"{version.Major}.{version.Minor}.{version.Build}";
            }

            private static MessageBoxIcon IconFor(NotificationSeverity severity) =>
                severity switch
                {
                    NotificationSeverity.Error => MessageBoxIcon.Error,
                    NotificationSeverity.Warning => MessageBoxIcon.Warning,
                    _ => MessageBoxIcon.Information
                };

            private static void LogFailure(string operation, NotificationError? error)
            {
                Debug.WriteLine(
                    $"Render Dock notification tray {operation} failed: " +
                    $"{error?.Code}: {error?.Message}");
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _owner.Shown -= Owner_Shown;
                _owner.Activated -= Owner_Activated;
                _owner.FormClosed -= Owner_FormClosed;
                _bell.Click -= Bell_Click;
                _toolTip.Dispose();
                _bell.Dispose();
            }
        }
    }
}
