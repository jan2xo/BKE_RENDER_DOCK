namespace BKE_MediaTools.Licensing
{
    internal static class AgentUpdateCoordinator
    {
        internal static void Attach(Form form, bool enterpriseSession)
        {
            if (enterpriseSession) return;
            form.Shown += async (_, __) => await CheckAfterShownAsync(form);
        }

        private static async Task CheckAfterShownAsync(Form form)
        {
            var client = new AgentUpdateClient();
            form.FormClosed += (_, __) => client.Dispose();
            var status = await client.StatusAsync();
            if (status == null || form.IsDisposed) return;
            if (status.State == "never_checked")
            {
                await client.QueueRefreshAsync(status);
                return;
            }
            if (!status.Available) return;
            form.BeginInvoke(new Action(() => ShowDialog(form, client, status)));
        }

        private static void ShowDialog(Form owner, AgentUpdateClient client, AgentUpdateStatus status)
        {
            using var dialog = new Form
            {
                Text = "Render Dock Update",
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MinimizeBox = false,
                MaximizeBox = false,
                ClientSize = new Size(430, 150)
            };
            var message = new Label { Left = 20, Top = 20, Width = 390, Height = 50, Text = $"Render Dock {status.LatestVersion} is available. Rendering can continue while you decide." };
            var later = new Button { Text = "Later", Left = 238, Top = 94, Width = 80, DialogResult = DialogResult.Cancel };
            var update = new Button { Text = "Update", Left = 326, Top = 94, Width = 80 };
            update.Click += async (_, __) =>
            {
                update.Enabled = false;
                try
                {
                    var result = await client.OpenCenterAsync(status);
                    MessageBox.Show(dialog, result, "BKE Update Center", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    dialog.Close();
                }
                catch
                {
                    MessageBox.Show(dialog, "The Licensing Agent Update Center is unavailable.", "BKE Update Center", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    update.Enabled = true;
                }
            };
            dialog.Controls.Add(message); dialog.Controls.Add(later); dialog.Controls.Add(update);
            dialog.CancelButton = later;
            dialog.ShowDialog(owner);
        }
    }
}

