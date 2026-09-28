using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace BKE_MediaTools.Licensing;

internal sealed class AgentRecoveryDialog : Form
{
    private const string RecoveryUrl = "https://jl-bke.com/licensing-agent";

    private AgentRecoveryDialog()
    {
        Text = "Licensing Agent unavailable";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(480, 190);

        var title = new Label { Text = "Licensing Agent unavailable", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Location = new Point(24, 22) };
        var body = new Label { Text = "The Licensing Agent is not running or could not be reached.\n\nInstall or repair the Licensing Agent, then reopen Render Dock.", AutoSize = true, Location = new Point(24, 58) };
        var download = new Button { Text = "Download Licensing Agent", AutoSize = true, Location = new Point(24, 132) };
        var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel, Location = new Point(310, 132) };
        download.Click += (_, _) => { if (OpenRecoveryPage()) Close(); else MessageBox.Show("Could not open the Licensing Agent download page automatically.\n\nVisit:\nhttps://jl-bke.com/licensing-agent", "Licensing Agent unavailable", MessageBoxButtons.OK, MessageBoxIcon.Information); };
        CancelButton = close;
        Controls.AddRange(new Control[] { title, body, download, close });
    }

    internal static void ShowRecovery()
    {
        using var dialog = new AgentRecoveryDialog();
        dialog.ShowDialog();
    }

    private static bool OpenRecoveryPage()
    {
        try { Process.Start(new ProcessStartInfo { FileName = RecoveryUrl, UseShellExecute = true }); return true; }
        catch (Exception) { return false; }
    }
}
