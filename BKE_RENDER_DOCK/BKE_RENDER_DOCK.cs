using Microsoft.VisualBasic.Devices;
using Microsoft.Win32;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Principal; // for UAC (IsAdministrator)
using System.Text; // <-- needed for UTF8 stderr capture
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Windows.Forms;
using static System.Collections.Specialized.BitVector32;


namespace BKE_MediaTools
{

    public partial class BKE_RenderDock : Form, IRenderDockInteraction
    {
        internal static class FfmpegBootstrap
        {
            public static readonly string TargetDir = @"C:\ffmpeg";
            public static readonly string FfmpegExe = Path.Combine(TargetDir, "bin", "ffmpeg.exe");

            // Two stable mirrors; we’ll try them in order.
            private static readonly string[] CandidateZips =
            {
        "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip",
        "https://www.gyan.dev/ffmpeg/builds/ffmpeg-git-essentials.zip"
        // (You can add a GitHub mirror later if you want a third option.)
    };

            /// <summary>
            /// Ensure C:\ffmpeg\bin\ffmpeg.exe exists. If missing, download and install.
            /// Elevates if C:\ write is blocked.
            /// </summary>
            public static void EnsurePresentOrOffer()
            {
                if (File.Exists(FfmpegExe)) return;

                var msg =
                    "ffmpeg was not found at C:\\ffmpeg\\bin\\ffmpeg.exe.\n\n" +
                    "Choose:\n" +
                    "  Yes    = Download & install now (may be slow)\n" +
                    "  No     = Exit; I'll install manually to C:\\ffmpeg\n" +
                    "  Cancel = Open download page in browser, then exit";

                var choice = MessageBox.Show(
                    msg, "BKE RenderDock",
                    MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2 // default to No (exit)
                );

                if (choice == DialogResult.Yes)
                {
                    try
                    {
                        DownloadAndInstall();
                        if (!File.Exists(FfmpegExe))
                            throw new InvalidOperationException("ffmpeg installation did not produce ffmpeg.exe");
                        return;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Need admin to write C:\ → relaunch elevated, then quit current proc
                        RelaunchAsAdministrator();
                        Environment.Exit(0);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(
                            "Auto-download failed:\n" + ex.Message +
                            "\n\nPlease install ffmpeg manually into C:\\ffmpeg then relaunch.",
                            "BKE RenderDock", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        Environment.Exit(1);
                    }
                }
                else if (choice == DialogResult.Cancel)
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "https://www.gyan.dev/ffmpeg/builds/",
                            UseShellExecute = true
                        });
                    }
                    catch { /* best effort */ }
                    Environment.Exit(0);
                }
                else
                {
                    // No: exit so user can install manually
                    Environment.Exit(0);
                }
            }


            private static void DownloadAndInstall()
            {
                Directory.CreateDirectory(TargetDir); // may throw UnauthorizedAccessException

                string zipPath = Path.Combine(Path.GetTempPath(), $"ffmpeg_{Guid.NewGuid():N}.zip");
                string extractDir = Path.Combine(Path.GetTempPath(), $"ffmpeg_extract_{Guid.NewGuid():N}");

                try
                {
                    // 1) Download
                    DownloadFirstWorkingZip(zipPath);

                    // 2) Extract to temp  (.NET 6+ has overwrite overload if you prefer)
                    ZipFile.ExtractToDirectory(zipPath, extractDir);

                    // 3) Find \bin\ffmpeg.exe in extracted tree
                    var ffmpegExeInZip = Directory
                        .EnumerateFiles(extractDir, "ffmpeg.exe", SearchOption.AllDirectories)
                        .FirstOrDefault() ?? throw new InvalidOperationException("Downloaded archive did not contain ffmpeg.exe");

                    var binDir = Path.GetDirectoryName(ffmpegExeInZip)!;      // ...\bin
                    var rootCandidate = Directory.GetParent(binDir)!.FullName; // archive root containing /bin

                    // 4) Copy all (bin, presets, etc.) into C:\ffmpeg (create/overwrite)
                    CopyAll(rootCandidate, TargetDir);
                }
                finally
                {
                    SafeDelete(zipPath);
                    SafeDeleteDir(extractDir);
                }
            }


            private static void DownloadFirstWorkingZip(string destZip)
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
                Exception? last = null;

                foreach (var url in CandidateZips)
                {
                    try
                    {
                        using var resp = http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).Result;
                        if (!resp.IsSuccessStatusCode) { last = new Exception($"HTTP {(int)resp.StatusCode} from {url}"); continue; }

                        using (var fs = File.Create(destZip))
                        {
                            resp.Content.CopyToAsync(fs).Wait();
                        }

                        // sanity: make sure it’s not junk
                        if (new FileInfo(destZip).Length < 5_000_000)  // ~5MB minimum
                            throw new IOException("Downloaded file too small to be ffmpeg zip.");

                        return; // success
                    }
                    catch (Exception ex)
                    {
                        last = ex;
                        SafeDelete(destZip);
                    }
                }

                throw new IOException("Failed to download ffmpeg from known mirrors.", last);
            }

            private static void CopyAll(string src, string dst)
            {
                foreach (var dir in Directory.EnumerateDirectories(src, "*", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(src, dir);
                    Directory.CreateDirectory(Path.Combine(dst, rel));
                }

                foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(src, file);
                    var target = Path.Combine(dst, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(file, target, overwrite: true);
                }
            }

            private static void SafeDelete(string path)
            {
                try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
            }

            private static void SafeDeleteDir(string path)
            {
                try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { /* ignore */ }
            }

            private static void RelaunchAsAdministrator()
            {
                var exe = Process.GetCurrentProcess().MainModule!.FileName!;
                var args = string.Join(" ", Environment.GetCommandLineArgs().Skip(1).Select(Quote));

                var psi = new ProcessStartInfo(exe, args)
                {
                    Verb = "runas",
                    UseShellExecute = true,
                    WorkingDirectory = Environment.CurrentDirectory
                };

                try { Process.Start(psi); }
                catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // user canceled UAC
                {
                    // Silent cancel: just return; the calling code will exit the non-admin instance.
                }
            }

            private static string Quote(string s) => s.Contains(' ') ? $"\"{s}\"" : s;
        }

        private readonly ConcurrentQueue<RenderJob> _queue = new();
        private bool _isWorking = false;
        private CancellationTokenSource? _cts;
        private readonly NotifyIcon _notify;

        public BKE_RenderDock()
        {
            InitializeComponent();
            AllowDrop = true;
            DragEnter += Form_DragEnter;
            DragDrop += Form_DragDrop;

            Directory.CreateDirectory(RenderEngine.OutputRoot);
            Directory.CreateDirectory(RenderEngine.TempRoot);

            _notify = new NotifyIcon { Icon = System.Drawing.SystemIcons.Application, Visible = true };
            FormClosed += (_, __) => _notify.Dispose();
        }

        // ======== DND ========
        private void Form_DragEnter(object? sender, DragEventArgs e)
        {
            if (e.Data!.GetDataPresent(DataFormats.FileDrop))
                e.Effect = DragDropEffects.Copy; // allow drops even while working; we will just enqueue
        }

        private void Notify(string title, string text, int ms = 3000)
        {
            if (InvokeRequired) { BeginInvoke(new Action(() => Notify(title, text, ms))); return; }
            _notify.BalloonTipIcon = ToolTipIcon.Info;
            _notify.BalloonTipTitle = title;
            _notify.BalloonTipText = text;
            _notify.ShowBalloonTip(ms);
        }


        private void Form_DragDrop(object? sender, DragEventArgs e)
        {
            try
            {
                string[] dropped = (string[])e.Data!.GetData(DataFormats.FileDrop)!;

                foreach (var job in RenderJobPlanner.Plan(dropped, this))
                    _queue.Enqueue(job);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Drop Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                EnsureRunner();
            }
        }

        // ======== JOB RUNNER ========
        private async Task ProcessQueueAsync(CancellationToken ct)
        {
            while (_queue.TryDequeue(out var job))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    Notify("BKE RenderDock", $"Starting: {job.Title} ({job.Type})");
                    await RenderEngine.ExecuteAsync(job, ct);
                    Notify("BKE RenderDock", $"Done: {job.Title}");
                }
                catch (Exception ex)
                {
                    Notify("BKE RenderDock (Error)", $"{job.Title}: {ex.Message}", 5000);
                    MessageBox.Show($"Job failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }

            System.Media.SystemSounds.Question.Play();
            Notify("BKE RenderDock", "All queued renders are done.", 5000);
        }

        // ======== HELPERS ========
        private void EnsureRunner()
        {
            if (_isWorking) return;
            _cts = new CancellationTokenSource();
            _isWorking = true;
            Task.Run(async () =>
            {
                try { await ProcessQueueAsync(_cts.Token); }
                finally { _isWorking = false; _cts?.Dispose(); _cts = null; }
            });
        }

        DetectedAudioChoice IRenderDockInteraction.ChooseDetectedAudio(IReadOnlyList<string> audios, string? bestAudio)
        {
            var dlg = MessageBox.Show(
                $"Found {audios.Count} audio file(s).\nUse best match?\n→ {Path.GetFileName(bestAudio)}",
                "Add background audio?",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question);

            return dlg == DialogResult.Yes
                ? DetectedAudioChoice.UseBest
                : dlg == DialogResult.No
                    ? DetectedAudioChoice.Browse
                    : DetectedAudioChoice.Cancel;
        }

        public bool OfferAudioFile()
        {
            return MessageBox.Show(
                "No audio detected. Browse one?",
                "Add audio?",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) == DialogResult.Yes;
        }

        public void ShowMessage(string text, string caption)
        {
            MessageBox.Show(text, caption);
        }

        public string? PromptForAudioFile()
        {
            using var ofd = new OpenFileDialog();
            ofd.Title = "Select audio file (optional)";
            ofd.Filter = "Audio Files|*.mp3;*.wav;*.m4a;*.aac;*.flac;*.ogg|All Files|*.*";
            ofd.Multiselect = false;
            return ofd.ShowDialog() == DialogResult.OK ? ofd.FileName : null;
        }

        LoopPolicy IRenderDockInteraction.GetLoopPolicy()
        {
            if (!RenderEngine.PromptForLoopPolicy) return RenderEngine.DefaultLoopPolicy;
            return AskLoopPolicy();
        }

        private static LoopPolicy AskLoopPolicy()
        {
            var dr = MessageBox.Show(
                "Audio detected. Choose loop mode:\n\nYes = Loop VIDEO to AUDIO\nNo = Loop AUDIO to VIDEO\nCancel = No looping (shortest)",
                "Audio Mode",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question);
            return dr == DialogResult.Yes ? LoopPolicy.LoopVideoToAudio :
                   dr == DialogResult.No ? LoopPolicy.LoopAudioToVideo :
                   LoopPolicy.Shortest;
        }

        public string? Prompt(string text, string caption)
        {
            return Microsoft.VisualBasic.Interaction.InputBox(text, caption, "");
        }

        private void BKE_RenderDock_Load(object sender, EventArgs e)
        {

        }

        private void BKE_RenderDock_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            // Ctrl+DoubleClick opens TEMP instead
            if ((ModifierKeys & Keys.Control) == Keys.Control)
            {
                Directory.CreateDirectory(RenderEngine.OutputRoot);
                Process.Start("explorer.exe", RenderEngine.OutputRoot);
                return;
            }

        }
    }
}
