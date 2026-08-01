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

    public partial class BKE_RenderDock : Form
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

        internal static class ExpiryLite
        {
            // brd.dat at %LOCALAPPDATA%
            private static readonly string FilePath =
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "brd.dat");

            private const string VERSION = "B1"; // format version marker

            /// <summary>
            /// Call once at startup. If brd.dat doesn't exist, creates a trial for N days.
            /// If it exists, enforces expiry + crude anti-clock-back.
            /// </summary>
            public static void InitializeOrCreateTrial(int trialDays)
            {
                var now = DateTime.UtcNow;

                if (!File.Exists(FilePath))
                {
                    var expiry = trialDays > 0 ? now.AddDays(trialDays) : now;
                    Save(expiry, now);
                    return;
                }

                var (exp, last, bind) = Load();

                // crude anti-clock-back (5 min tolerance)
                if (now.AddMinutes(5) < last)
                    Fail("System clock manipulation detected.");

                // forward-only lastSeen
                if (now > last)
                    Save(exp, now, bind);

                if (now > exp)
                    Fail("This copy has expired.");
            }

            /// <summary>One-time setter (e.g., via hidden CLI): writes the new expiry.</summary>
            public static void SetExpiryUtc(DateTime expiryUtc)
            {
                var now = DateTime.UtcNow;
                Save(expiryUtc.ToUniversalTime(), now);
            }

            public static DateTime GetExpiryUtc()
            {
                var (exp, _, _) = Load();
                return exp;
            }

            // ---------------- internals ----------------

            private static (DateTime exp, DateTime last, string bind) Load()
            {
                try
                {
                    var raw = File.ReadAllBytes(FilePath);
                    var text = Encoding.UTF8.GetString(Convert.FromBase64String(Encoding.UTF8.GetString(raw)));

                    // Format: VERSION|expTicks|lastTicks|bindHex
                    var parts = text.Split('|');
                    if (parts.Length != 4 || parts[0] != VERSION)
                        throw new InvalidDataException("Invalid brd.dat format.");

                    var exp = new DateTime(long.Parse(parts[1]), DateTimeKind.Utc);
                    var last = new DateTime(long.Parse(parts[2]), DateTimeKind.Utc);
                    var bind = parts[3];

                    // machine/user bind check
                    if (bind != GetBinding())
                        throw new InvalidDataException("brd.dat is not for this machine/user.");

                    return (exp, last, bind);
                }
                catch (Exception ex)
                {
                    Fail("brd.dat unreadable or invalid.\n" + ex.Message);
                    throw; // never reached (Fail exits)
                }
            }

            private static void Save(DateTime expiryUtc, DateTime lastSeenUtc, string? existingBind = null)
            {
                var bind = existingBind ?? GetBinding();
                var payload = string.Join("|", VERSION, expiryUtc.Ticks.ToString(), lastSeenUtc.Ticks.ToString(), bind);

                // Light obfuscation: UTF8 -> Base64 -> UTF8 (no DPAPI)
                var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));
                var outBytes = Encoding.UTF8.GetBytes(b64);

                var dir = Path.GetDirectoryName(FilePath)!;

                // 0) Ensure parent dir exists and is writable
                Directory.CreateDirectory(dir);
                using (var fs = new FileStream(
                    Path.Combine(dir, ".$perm_test"),
                    FileMode.OpenOrCreate, FileAccess.Write, FileShare.None,
                    bufferSize: 1, FileOptions.DeleteOnClose)) { /* if this fails, you truly can't write here */ }

                // 1) If "brd.dat" is a directory, bail with a clear message
                if (Directory.Exists(FilePath))
                {
                    MessageBox.Show(
                        $"'{FilePath}' is a FOLDER, not a file. Delete or rename that folder, then relaunch.",
                        "BKE RenderDock",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    Environment.Exit(1);
                }

                // 2) Clear read-only/system attributes so we can overwrite
                if (File.Exists(FilePath))
                {
                    try
                    {
                        var attr = File.GetAttributes(FilePath);
                        var cleared = attr & ~(FileAttributes.ReadOnly | FileAttributes.System);
                        if (cleared != attr) File.SetAttributes(FilePath, cleared);
                    }
                    catch { /* best effort */ }
                }

                // 3) Atomic write: write to temp, then replace/move
                var tmp = Path.Combine(dir, ".$brd  .tmp");
                File.WriteAllBytes(tmp, outBytes);

                try
                {
                    if (File.Exists(FilePath))
                        File.Replace(tmp, FilePath, null);   // atomic replace if destination exists
                    else
                        File.Move(tmp, FilePath);            // first write
                }
                catch (IOException)
                {
                    // Fallback if Replace not supported (e.g., different volume)
                    if (File.Exists(FilePath)) File.Delete(FilePath);
                    File.Move(tmp, FilePath);
                }
                finally
                {
                    if (File.Exists(tmp)) { try { File.Delete(tmp); } catch { } }
                }

                // 4) Hide it (cosmetic)
                try { File.SetAttributes(FilePath, FileAttributes.Hidden); } catch { /* best effort */ }
            }


            private static void Fail(string reason)
            {
                MessageBox.Show($"{reason}\n\nContact support.", "BKE RenderDock", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Environment.Exit(1);
            }

            // Bind to machine + user so a copied file won't work elsewhere
            private static string GetBinding()
            {
                try
                {
                    using var lm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                    using var k = lm.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
                    var machineGuid = k?.GetValue("MachineGuid")?.ToString() ?? "noguid";
                    var sid = System.Security.Principal.WindowsIdentity.GetCurrent()?.User?.Value ?? "nosid";

                    using var sha = SHA256.Create();
                    var hash = sha.ComputeHash(Encoding.UTF8.GetBytes($"{machineGuid}|{sid}"));
                    return Convert.ToHexString(hash);
                }
                catch
                {
                    return "UNKNOWN_BIND";
                }
            }

            // Optional helper: parse a CLI switch to set expiry quickly
            public static bool TryHandleCli()
            {
                var args = Environment.GetCommandLineArgs();

                // --- show expiry ---
                if (args.Any(a => a.Equals("--show-expiry", StringComparison.OrdinalIgnoreCase)))
                {
                    if (!File.Exists(FilePath))
                    {
                        MessageBox.Show("brd.dat not found. No expiry is set yet.",
                            "BKE RenderDock", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return true; // handled
                    }

                    try
                    {
                        var exp = GetExpiryUtc();
                        var now = DateTime.UtcNow;
                        var remaining = exp - now;
                        var remainText = remaining.TotalSeconds <= 0
                            ? "Expired."
                            : $"{remaining.Days}d {remaining.Hours}h {remaining.Minutes}m remaining";

                        MessageBox.Show(
                            $"Expiry (UTC): {exp:u}\nNow   (UTC): {now:u}\n\nStatus: {remainText}",
                            "BKE RenderDock",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Could not read brd.dat:\n" + ex.Message,
                            "BKE RenderDock", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    return true; // handled
                }

                // --- set expiry explicitly ---
                const string setKey = "--set-expiry=";
                var setArg = args.FirstOrDefault(a => a.StartsWith(setKey, StringComparison.OrdinalIgnoreCase));
                if (setArg != null)
                {
                    var s = setArg.Substring(setKey.Length);
                    if (!DateTime.TryParse(s, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var dt))
                    {
                        MessageBox.Show($"Invalid datetime: {s}",
                            "BKE RenderDock", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return true;
                    }
                    if (dt.Kind != DateTimeKind.Utc) dt = DateTime.SpecifyKind(dt, DateTimeKind.Utc);

                    SetExpiryUtc(dt);
                    MessageBox.Show($"Expiry set to {dt:u}",
                        "BKE RenderDock", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return true;
                }

                // --- optional: extend by N days from current expiry ---
                const string extKey = "--extend-days=";
                var extArg = args.FirstOrDefault(a => a.StartsWith(extKey, StringComparison.OrdinalIgnoreCase));
                if (extArg != null)
                {
                    if (!int.TryParse(extArg.Substring(extKey.Length), out var days) || days == 0)
                    {
                        MessageBox.Show("Use a non-zero integer for --extend-days=N",
                            "BKE RenderDock", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return true;
                    }

                    if (!File.Exists(FilePath))
                    {
                        MessageBox.Show("brd.dat not found. Set an expiry first with --set-expiry=...",
                            "BKE RenderDock", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return true;
                    }

                    var curr = GetExpiryUtc();
                    var next = curr.AddDays(days);
                    SetExpiryUtc(next);
                    MessageBox.Show($"Expiry changed:\nOld: {curr:u}\nNew: {next:u}",
                        "BKE RenderDock", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return true;
                }

                return false; // nothing handled
            }

        }

        // ======== CONFIG ========

        private static class AppConfig
        {
            public static readonly string FFmpegPath = @"C:\\ffmpeg\\bin\\ffmpeg.exe"; // adjust if needed

            // Dynamically resolved roots (prefer D:\, fall back to C:\)
            public static readonly string OutputRoot;
            public static readonly string TempRoot;

            public const int Fps = 30;
            public const int SecondsPerImage = 3; // each image shows ~3s
            public const bool AlwaysPromptForAudioOnSingleVideo = true;
            public const bool AlwaysPromptForAudioOnSlideshow = true;
            public const bool AlwaysPromptForAudioOnCombineVideos = true;
            public const bool AlwaysPromptForAudioOnMixed = true;
            public const bool PromptForLoopPolicy = true;
            public static readonly LoopPolicy DefaultLoopPolicy = LoopPolicy.Shortest;
            public const string OutputCodec = "h264_nvenc"; // fall back to libx264 if no NVENC


            // Prefer a ready, fixed D:\ drive; else C:\
            private static string SelectRoot()
            {
                var d = DriveInfo
                    .GetDrives()
                    .FirstOrDefault(dr =>
                        dr.IsReady &&
                        dr.DriveType == DriveType.Fixed &&
                        dr.Name.StartsWith("D:", StringComparison.OrdinalIgnoreCase));
                return d?.Name ?? @"C:\";
            }

            // Create needed folders; elevate if blocked by permissions
            private static void EnsurePathsOrElevate()
            {
                try
                {
                    Directory.CreateDirectory(AppConfig.OutputRoot);
                    Directory.CreateDirectory(AppConfig.TempRoot);
                }
                catch (UnauthorizedAccessException)
                {
                    if (!IsAdministrator())
                    {
                        // Ask for admin, then quit this instance
                        RelaunchAsAdministrator();
                        Environment.Exit(0);
                    }
                    else
                    {
                        // Already admin but still blocked → surface the error
                        throw;
                    }
                }
            }

            private static bool IsAdministrator()
            {
                using var id = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(id);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }

            private static void RelaunchAsAdministrator()
            {
                var exe = Process.GetCurrentProcess().MainModule!.FileName!;
                var args = string.Join(" ",
                    Environment.GetCommandLineArgs().Skip(1).Select(Quote));

                var psi = new ProcessStartInfo(exe, args)
                {
                    Verb = "runas",            // triggers UAC
                    UseShellExecute = true,
                    WorkingDirectory = Environment.CurrentDirectory
                };

                try
                {
                    Process.Start(psi);
                }
                catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // user canceled UAC
                {
                    MessageBox.Show("Administrator permission is required to create output folders.",
                                    "BKE RenderDock", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }

            private static string Quote(string s) => s.Contains(' ') ? $"\"{s}\"" : s;



            static AppConfig()
            {
                string root = SelectRoot(); // D:\ if available; else C:\
                OutputRoot = Path.Combine(root, "BKE_RENDER_DOCK");
                TempRoot = Path.Combine(OutputRoot, "TEMP");
                EnsurePathsOrElevate();
            }
        }




        // ======== TYPES ========
        private enum JobType { SlideshowFromFolder, SlideshowFromImages, CombineVideos, AddAudioToVideo, TranscodeSingleVideo, SlideshowThenVideos, SaveProcessedImage }
        private enum LoopPolicy { Shortest, LoopVideoToAudio, LoopAudioToVideo }
        private record WorkItem(JobType Type, string Title, string OutputFolder, string SessionFolder, List<string> Inputs, string? AudioPath = null, List<string>? ExtraVideos = null, LoopPolicy? LoopMode = null);

        private readonly ConcurrentQueue<WorkItem> _queue = new();
        private bool _isWorking = false;
        private CancellationTokenSource? _cts;
        private readonly NotifyIcon _notify;

        public BKE_RenderDock()
        {
            InitializeComponent();
            AllowDrop = true;
            DragEnter += Form_DragEnter;
            DragDrop += Form_DragDrop;

            Directory.CreateDirectory(AppConfig.OutputRoot);
            Directory.CreateDirectory(AppConfig.TempRoot);

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
                // allow enqueue while working
                string[] dropped = (string[])e.Data!.GetData(DataFormats.FileDrop)!;

                var images = new List<string>();
                var videos = new List<string>();
                var audios = new List<string>();

                foreach (var path in dropped)
                {
                    if (Directory.Exists(path))
                    {
                        string folderName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                        // temp session path (assigned only if needed)
                        string stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                        string session = Path.Combine(AppConfig.TempRoot, $"{stamp}_{Sanitize(folderName)}");
                        

                        var files = Directory.GetFiles(path);
                        var imgs = files.Where(IsImageFile).OrderBy(p => p).ToList();
                        var vids = files.Where(IsVideoFile).OrderBy(p => p).ToList();
                        var auds = files.Where(IsAudioFile).OrderBy(p => p).ToList();
                        // If folder has nothing useful, bail fast (no temp folder created)
                        if (imgs.Count == 0 && vids.Count == 0)
                        {
                            if (auds.Any())
                                MessageBox.Show($"“{folderName}” has only audio. Nothing to render.", "Heads up");
                            continue;
                        }
                        else
                        {
                            Directory.CreateDirectory(session); // create temp session folder only if needed
                        }

                        // If folder has both images and videos: build slideshow first, then append videos
                        if (imgs.Count > 0 && vids.Count > 0)
                        {
                            var processed = new List<string>();
                            for (int i = 0; i < imgs.Count; i++)
                            {
                                processed.Add(ProcessImage(imgs[i], i + 1, session));
                            }

                            _queue.Enqueue(new WorkItem(
                                JobType.SlideshowThenVideos,
                                folderName,
                                EnsureDatedOutput(),
                                session,
                                processed,
                                ChooseBestAudio(auds),
                                vids,
                                auds.Any() ? GetLoopPolicy() : null
                            ));
                            continue; // don't double-queue
                        }

                        // Only images → slideshow (optional audio if present)
                        if (imgs.Count >= 1 && vids.Count == 0)
                        {
                            var processed = new List<string>();
                            for (int i = 0; i < imgs.Count; i++)
                            {
                                processed.Add(ProcessImage(imgs[i], i + 1, session));
                            }
                            _queue.Enqueue(new WorkItem(
                                JobType.SlideshowFromFolder, 
                                folderName, 
                                EnsureDatedOutput(), 
                                session, 
                                processed, 
                                auds.FirstOrDefault(), null,
                                auds.Any() ? GetLoopPolicy() : null
                                ));
                            continue;
                        }

                        // Only videos → concat / transcode
                        if (vids.Count >= 2)
                        {  
                            string? audioPath = auds.FirstOrDefault();
                            _queue.Enqueue(new WorkItem(JobType.CombineVideos,
                                folderName + "_combined",
                                EnsureDatedOutput(),
                                "",
                                vids,
                                audioPath,
                                null,
                                audioPath != null ? GetLoopPolicy() : null));
                            continue;
                        }
                        else if (vids.Count == 1)
                        {
                            string? audioPath = auds.Count > 0 ? ChooseBestAudio(auds) : null;
                            if (!string.IsNullOrEmpty(audioPath))
                            {
                                _queue.Enqueue(new WorkItem(JobType.AddAudioToVideo, Path.GetFileNameWithoutExtension(vids[0]), EnsureDatedOutput(), "", new List<string> { vids[0] }, audioPath, null, GetLoopPolicy()));
                                continue;
                            }
                            else
                            {
                                _queue.Enqueue(new WorkItem(JobType.TranscodeSingleVideo, Path.GetFileNameWithoutExtension(vids[0]), EnsureDatedOutput(), "", new List<string> { vids[0] }));
                                continue;
                            }
                        }
                    }
                    else if (File.Exists(path))
                    {
                        if (IsImageFile(path)) images.Add(path);
                        else if (IsVideoFile(path)) videos.Add(path);
                        else if (IsAudioFile(path)) audios.Add(path);
                        continue;
                    }
                }

                // Mixed-file logic
                images = images.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p).ToList();
                videos = videos.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p).ToList();
                audios = audios.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p).ToList();

                if (images.Count > 0 && videos.Count > 0)
                {
                    // MIXED: slideshow first, then append videos
                    string title = Prompt("Enter the project title:", "BKE MIX");
                    if (!string.IsNullOrWhiteSpace(title)) 
                    {
                        string stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                        string session = Path.Combine(AppConfig.TempRoot, $"{stamp}_{Sanitize(title)}");
                        Directory.CreateDirectory(session);

                        var processed = new List<string>();
                        for (int i = 0; i < images.Count; i++)
                            processed.Add(ProcessImage(images[i], i + 1, session)); // ensure TEMP0001.jpg pattern if your ffmpeg expects it

                        // Audio selection (optional)
                        string? audioPath = null;
                        if (audios.Any())
                        {
                            var best = ChooseBestAudio(audios);
                            var dlg = MessageBox.Show(
                                $"Found {audios.Count} audio file(s).\nUse best match?\n→ {Path.GetFileName(best)}",
                                "Add background audio?",
                                MessageBoxButtons.YesNoCancel,
                                MessageBoxIcon.Question);

                            if (dlg == DialogResult.Yes) audioPath = best;
                            else if (dlg == DialogResult.No) audioPath = PromptForAudioFile(); // may be null
                            else return; // Cancel the whole mixed job if user hit Cancel
                        }
                        else if (AppConfig.AlwaysPromptForAudioOnMixed)
                        {
                            if (MessageBox.Show("No audio detected. Browse one?", "Add audio?",
                                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                                audioPath = PromptForAudioFile();
                        }

                        var policy = audioPath != null ? GetLoopPolicy() : (LoopPolicy?)null;

                        _queue.Enqueue(new WorkItem(
                            JobType.SlideshowThenVideos,
                            title,
                            EnsureDatedOutput(),
                            session,
                            processed,
                            audioPath,
                            videos,
                            policy
                        ));
                        return;
                    }
                }
                else if (images.Count == 1 && videos.Count == 0)
                {
                    // SINGLE IMAGE
                    string title = Prompt("Enter the image name:", "BKE Image");
                    if (!string.IsNullOrWhiteSpace(title))
                    {
                        string stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                        string session = Path.Combine(AppConfig.TempRoot, $"{stamp}_{Sanitize(title)}");
                        Directory.CreateDirectory(session);

                        _queue.Enqueue(new WorkItem(
                            JobType.SaveProcessedImage,
                            title,
                            EnsureDatedOutput(),
                            session,
                            new List<string> { images[0] }
                        ));
                        return;
                    }
                }
                else if (images.Count >= 2 && videos.Count == 0)
                {
                    // IMAGES ONLY → SLIDESHOW
                    string title = Prompt("Enter the slideshow title:", "BKE SLIDESHOW");
                    if (!string.IsNullOrWhiteSpace(title))
                    {
                        string stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                        string session = Path.Combine(AppConfig.TempRoot, $"{stamp}_{Sanitize(title)}");
                        Directory.CreateDirectory(session);

                        var processed = new List<string>();
                        for (int i = 0; i < images.Count; i++)
                            processed.Add(ProcessImage(images[i], i + 1, session));

                        string? audioPath = audios.FirstOrDefault();
                        if (audioPath == null && AppConfig.AlwaysPromptForAudioOnSlideshow)
                            audioPath = PromptForAudioFile();

                        _queue.Enqueue(new WorkItem(
                            JobType.SlideshowFromImages,
                            title,
                            EnsureDatedOutput(),
                            session,
                            processed,
                            audioPath,
                            null,
                            audioPath != null ? GetLoopPolicy() : null
                        ));
                        return;
                    }
                }
                else if (videos.Count >= 2 && images.Count == 0)
                {
                    // MULTI-VIDEO
                    string outputName = Prompt("Enter the combined video title:", "CombinedVideo") ?? "CombinedVideo";

                    string? audioPath = audios.FirstOrDefault();
                    if (audioPath == null && AppConfig.AlwaysPromptForAudioOnCombineVideos)
                        audioPath = PromptForAudioFile();

                    _queue.Enqueue(new WorkItem(
                        JobType.CombineVideos,
                        outputName,
                        EnsureDatedOutput(),
                        "",      // no image session needed
                        videos,
                        audioPath,
                        null,
                        audioPath != null ? GetLoopPolicy() : null
                    ));
                    return;
                }
                else if (videos.Count == 1 && images.Count == 0)
                {
                    // SINGLE VIDEO
                    string? audioPath = audios.FirstOrDefault();
                    if (audioPath == null && AppConfig.AlwaysPromptForAudioOnSingleVideo)
                        audioPath = PromptForAudioFile();

                    if (!string.IsNullOrEmpty(audioPath))
                    {
                        _queue.Enqueue(new WorkItem(
                            JobType.AddAudioToVideo,
                            Path.GetFileNameWithoutExtension(videos[0]),
                            EnsureDatedOutput(),
                            "",
                            new List<string> { videos[0] },
                            audioPath,
                            null,
                            GetLoopPolicy()
                        ));
                        return;
                    }
                    else
                    {
                        _queue.Enqueue(new WorkItem(
                            JobType.TranscodeSingleVideo,
                            Path.GetFileNameWithoutExtension(videos[0]),
                            EnsureDatedOutput(),
                            "",
                            new List<string> { videos[0] }
                        ));
                        return;
                    }
                }
                else
                {
                    // Only audio or nothing recognized
                    if (audios.Any())
                        MessageBox.Show("Only audio files were dropped. Nothing to render.", "Heads up");
                    return;
                }

            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Drop Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                EnsureRunner(); // ALWAYS attempt to start the runner
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
                    switch (job.Type)
                    {
                        case JobType.SlideshowFromFolder:
                        case JobType.SlideshowFromImages:
                            await BuildSlideshowAsync(job, ct);
                            break;
                        case JobType.CombineVideos:
                            await CombineVideosAsync(job, ct);
                            break;
                        case JobType.SlideshowThenVideos:
                            await BuildSlideshowThenVideosAsync(job, ct);
                            break;
                        case JobType.SaveProcessedImage:
                            await SaveProcessedImageAsync(job, ct);
                            break;
                        case JobType.AddAudioToVideo:
                            await AddAudioToVideoAsync(job, ct);
                            break;
                        case JobType.TranscodeSingleVideo:
                            await TranscodeSingleVideoAsync(job, ct);
                            break;
                    }
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

        // ======== OPERATIONS ========
        private async Task SaveProcessedImageAsync(WorkItem job, CancellationToken ct)
        {
            // job.Inputs[0] is the original image path
            string src = job.Inputs[0];
            string processed = ProcessImage(src, 1, job.SessionFolder);
            string outPath = Path.Combine(job.OutputFolder, $"{Sanitize(job.Title)}.jpg");
            try { if (File.Exists(outPath)) File.Delete(outPath); } catch { }
            File.Move(processed, outPath);
            TryDeleteFolder(job.SessionFolder);
            await Task.CompletedTask;
        }

        private async Task BuildSlideshowAsync(WorkItem job, CancellationToken ct)
        {
            if (job.Inputs.Count == 0) return;

            string tempSlide = Path.Combine(job.SessionFolder, "_slideshow.mp4");
            string pattern = Path.Combine(job.SessionFolder, "TEMP%04d.jpg");
            int totalSeconds = job.Inputs.Count * AppConfig.SecondsPerImage;
            string vf = $"scale=8000:-1,setsar=1,zoompan=z='zoom+0.001':x=iw/2-(iw/zoom/2):y=ih/2-(ih/zoom/2):d={AppConfig.Fps * AppConfig.SecondsPerImage}:s=1920x1080:fps={AppConfig.Fps}";

            // 1) Render slideshow video (silent)
            string slideArgs = $"-y -nostdin -hide_banner -loglevel error -nostats -framerate {AppConfig.Fps} -i \"{pattern}\" -vf \"{vf}\" -t {totalSeconds} -c:v {AppConfig.OutputCodec} -pix_fmt yuv420p -max_muxing_queue_size 2048 \"{tempSlide}\"";
            await RunFfmpegAsync(slideArgs, ct);

            string outputPath = Path.Combine(job.OutputFolder, $"{Sanitize(job.Title)}.mp4");

            // 2) If audio is present, mux with chosen loop policy; else move/copy
            if (!string.IsNullOrWhiteSpace(job.AudioPath))
            {
                var mode = job.LoopMode ?? AppConfig.DefaultLoopPolicy;
                string args;
                switch (mode)
                {
                    case LoopPolicy.LoopVideoToAudio:
                        args = $"-y -nostdin -hide_banner -loglevel error -nostats -stream_loop -1 -i \"{tempSlide}\" -i \"{job.AudioPath}\" -shortest -map 0:v -map 1:a -c:v {AppConfig.OutputCodec} -pix_fmt yuv420p -c:a aac -b:a 192k -ar 48000 -movflags +faststart \"{outputPath}\"";
                        break;
                    case LoopPolicy.LoopAudioToVideo:
                        args = $"-y -nostdin -hide_banner -loglevel error -nostats -i \"{tempSlide}\" -stream_loop -1 -i \"{job.AudioPath}\" -shortest -map 0:v -map 1:a -c:v copy -c:a aac -b:a 192k -ar 48000 -movflags +faststart \"{outputPath}\"";
                        break;
                    default:
                        args = $"-y -nostdin -hide_banner -loglevel error -nostats -i \"{tempSlide}\" -i \"{job.AudioPath}\" -shortest -map 0:v -map 1:a -c:v copy -c:a aac -b:a 192k -ar 48000 -movflags +faststart \"{outputPath}\"";
                        break;
                }
                await RunFfmpegAsync(args, ct);
            }
            else
            {
                // no audio -> temp is final
                try { if (File.Exists(outputPath)) File.Delete(outputPath); } catch { }
                File.Move(tempSlide, outputPath);
            }

            TryDeleteFolder(job.SessionFolder);
        }

        private async Task BuildSlideshowThenVideosAsync(WorkItem job, CancellationToken ct)
        {
            if (job.Inputs.Count == 0 || job.ExtraVideos == null || job.ExtraVideos.Count == 0) return;

            // 1) Render slideshow MP4 (silent)
            string slideshowPath = Path.Combine(job.SessionFolder, "_slideshow.mp4");
            int totalSeconds = job.Inputs.Count * AppConfig.SecondsPerImage;
            string pattern = Path.Combine(job.SessionFolder, "TEMP%04d.jpg");
            string vf = $"scale=8000:-1,setsar=1,zoompan=z='zoom+0.001':x=iw/2-(iw/zoom/2):y=ih/2-(ih/zoom/2):d={AppConfig.Fps * AppConfig.SecondsPerImage}:s=1920x1080:fps={AppConfig.Fps}";
            string slideArgs = $"-y -nostdin -hide_banner -loglevel error -nostats -framerate {AppConfig.Fps} -i \"{pattern}\" -vf \"{vf}\" -t {totalSeconds} -c:v {AppConfig.OutputCodec} -pix_fmt yuv420p -max_muxing_queue_size 2048 \"{slideshowPath}\"";
            await RunFfmpegAsync(slideArgs, ct);

            // 2) Normalize each video
            var normalized = new List<string> { slideshowPath };
            int idx = 0;
            foreach (var v in job.ExtraVideos)
            {
                string outVid = Path.Combine(job.SessionFolder, $"_norm_{idx++:000}.mp4");
                string normArgs = $"-y -nostdin -hide_banner -loglevel error -nostats -i \"{v}\" -vf scale=1920:1080:force_original_aspect_ratio=decrease,pad=1920:1080:(ow-iw)/2:(oh-ih)/2,fps={AppConfig.Fps} -c:v {AppConfig.OutputCodec} -pix_fmt yuv420p -vsync cfr -an -max_muxing_queue_size 2048 \"{outVid}\"";
                await RunFfmpegAsync(normArgs, ct);
                normalized.Add(outVid);
            }

            // 3) Concat list
            string listPath = Path.Combine(job.SessionFolder, "_concat.txt");
            File.WriteAllLines(listPath, normalized.Select(p => $"file '{p.Replace("'", "'\\''")}'"));

            string joinedPath = Path.Combine(job.SessionFolder, "_joined.mp4");
            string concatArgs = $"-y -nostdin -hide_banner -loglevel error -nostats -f concat -safe 0 -i \"{listPath}\" -c copy -an \"{joinedPath}\"";
            await RunFfmpegAsync(concatArgs, ct);

            string outputPath = Path.Combine(job.OutputFolder, $"{Sanitize(job.Title)}.mp4");

            if (!string.IsNullOrWhiteSpace(job.AudioPath))
            {
                var mode = job.LoopMode ?? AppConfig.DefaultLoopPolicy;
                string finalArgs;
                switch (mode)
                {
                    case LoopPolicy.LoopVideoToAudio:
                        finalArgs = $"-y -nostdin -hide_banner -loglevel error -nostats -stream_loop -1 -i \"{joinedPath}\" -i \"{job.AudioPath}\" -shortest -map 0:v -map 1:a -c:v {AppConfig.OutputCodec} -pix_fmt yuv420p -c:a aac -b:a 192k -ar 48000 -movflags +faststart \"{outputPath}\"";
                        break;
                    case LoopPolicy.LoopAudioToVideo:
                        finalArgs = $"-y -nostdin -hide_banner -loglevel error -nostats -i \"{joinedPath}\" -stream_loop -1 -i \"{job.AudioPath}\" -shortest -map 0:v -map 1:a -c:v copy -c:a aac -b:a 192k -ar 48000 -movflags +faststart \"{outputPath}\"";
                        break;
                    default:
                        finalArgs = $"-y -nostdin -hide_banner -loglevel error -nostats -i \"{joinedPath}\" -i \"{job.AudioPath}\" -shortest -map 0:v -map 1:a -c:v copy -c:a aac -b:a 192k -ar 48000 -movflags +faststart \"{outputPath}\"";
                        break;
                }
                await RunFfmpegAsync(finalArgs, ct);
            }
            else
            {
                try { if (File.Exists(outputPath)) File.Delete(outputPath); } catch { }
                File.Move(joinedPath, outputPath);
            }

            TryDeleteFolder(job.SessionFolder);
        }

        private async Task CombineVideosAsync(WorkItem job, CancellationToken ct)
        {
            // 0) Make a session folder if none was provided
            string session = string.IsNullOrWhiteSpace(job.SessionFolder)
                ? Path.Combine(AppConfig.TempRoot, $"comb_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}")
                : job.SessionFolder;
            Directory.CreateDirectory(session);

            // 1) Normalize EACH input first (re-encode to match res/FPS; strip audio so streams match)
            var normalized = new List<string>();
            int idx = 0;
            foreach (var v in job.Inputs)
            {
                string outVid = Path.Combine(session, $"_norm_{idx++:000}.mp4");
                string normArgs =
                    $"-y -nostdin -hide_banner -loglevel error -nostats " +
                    $"-i \"{v}\" " +
                    $"-vf scale=1920:1080:force_original_aspect_ratio=decrease," +
                    $"pad=1920:1080:(ow-iw)/2:(oh-ih)/2,fps={AppConfig.Fps} " +
                    $"-c:v {AppConfig.OutputCodec} -pix_fmt yuv420p -vsync cfr -an " +
                    $"-max_muxing_queue_size 2048 \"{outVid}\"";
                await RunFfmpegAsync(normArgs, ct);
                normalized.Add(outVid);
            }

            // 2) Concat the normalized clips (fast, no re-encode, and no audio track)
            string listPath = Path.Combine(session, "_concat.txt");
            File.WriteAllLines(listPath, normalized.Select(p => $"file '{p.Replace("'", "'\\''")}'"));

            string joinedPath = Path.Combine(session, "_joined.mp4");
            string concatArgs =
                $"-y -nostdin -hide_banner -loglevel error -nostats " +
                $"-f concat -safe 0 -i \"{listPath}\" -c copy -an \"{joinedPath}\"";
            await RunFfmpegAsync(concatArgs, ct);

            // 3) Finalize: either attach music (with your chosen loop policy) or just save the joined video
            string outputPath = Path.Combine(job.OutputFolder, $"{Sanitize(job.Title)}_{DateTime.Now:yyyyMMddHHmmss}.mp4");
            if (!string.IsNullOrWhiteSpace(job.AudioPath))
            {
                var mode = job.LoopMode ?? AppConfig.DefaultLoopPolicy;
                string finalArgs;
                switch (mode)
                {
                    case LoopPolicy.LoopVideoToAudio:
                        // repeat video to match audio → must re-encode video
                        finalArgs =
                            $"-y -nostdin -hide_banner -loglevel error -nostats " +
                            $"-stream_loop -1 -i \"{joinedPath}\" -i \"{job.AudioPath}\" -shortest " +
                            $"-map 0:v -map 1:a -c:v {AppConfig.OutputCodec} -pix_fmt yuv420p " +
                            $"-c:a aac -b:a 192k -ar 48000 -movflags +faststart \"{outputPath}\"";
                        break;

                    case LoopPolicy.LoopAudioToVideo:
                        // repeat audio to match video → keep video, encode audio only
                        finalArgs =
                            $"-y -nostdin -hide_banner -loglevel error -nostats " +
                            $"-i \"{joinedPath}\" -stream_loop -1 -i \"{job.AudioPath}\" -shortest " +
                            $"-map 0:v -map 1:a -c:v copy -c:a aac -b:a 192k -ar 48000 " +
                            $"-movflags +faststart \"{outputPath}\"";
                        break;

                    default: // Shortest
                        finalArgs =
                            $"-y -nostdin -hide_banner -loglevel error -nostats " +
                            $"-i \"{joinedPath}\" -i \"{job.AudioPath}\" -shortest " +
                            $"-map 0:v -map 1:a -c:v copy -c:a aac -b:a 192k -ar 48000 " +
                            $"-movflags +faststart \"{outputPath}\"";
                        break;
                }
                await RunFfmpegAsync(finalArgs, ct);
            }
            else
            {
                try { if (File.Exists(outputPath)) File.Delete(outputPath); } catch { }
                File.Move(joinedPath, outputPath);
            }

            // 4) Cleanup
            TryDeleteFile(listPath);
            TryDeleteFolder(session);
        }


        private async Task AddAudioToVideoAsync(WorkItem job, CancellationToken ct)
        {
            string video = job.Inputs[0];
            string audio = job.AudioPath!;
            var mode = job.LoopMode ?? AppConfig.DefaultLoopPolicy;
            string outputPath = Path.Combine(job.OutputFolder, $"{Sanitize(Path.GetFileNameWithoutExtension(video))}_withAudio.mp4");

            string args;
            switch (mode)
            {
                case LoopPolicy.LoopVideoToAudio:
                    // repeat video to match audio length
                    args = $"-y -nostdin -hide_banner -loglevel error -nostats -stream_loop -1 -i \"{video}\" -i \"{audio}\" -shortest -map 0:v -map 1:a -c:v {AppConfig.OutputCodec} -pix_fmt yuv420p -c:a aac -b:a 192k -ar 48000 -movflags +faststart \"{outputPath}\"";
                    break;
                case LoopPolicy.LoopAudioToVideo:
                    // repeat audio to match video length
                    args = $"-y -nostdin -hide_banner -loglevel error -nostats -i \"{video}\" -stream_loop -1 -i \"{audio}\" -shortest -map 0:v -map 1:a -c:v {AppConfig.OutputCodec} -pix_fmt yuv420p -c:a aac -b:a 192k -ar 48000 -movflags +faststart \"{outputPath}\"";
                    break;
                default:
                    // shortest
                    args = $"-y -nostdin -hide_banner -loglevel error -nostats -i \"{video}\" -i \"{audio}\" -shortest -map 0:v -map 1:a -c:v {AppConfig.OutputCodec} -pix_fmt yuv420p -c:a aac -b:a 192k -ar 48000 -movflags +faststart \"{outputPath}\"";
                    break;
            }

            await RunFfmpegAsync(args, ct);
        }

        private async Task TranscodeSingleVideoAsync(WorkItem job, CancellationToken ct)
        {
            string video = job.Inputs[0];
            string outputPath = Path.Combine(job.OutputFolder, $"{Sanitize(Path.GetFileNameWithoutExtension(video))}.mp4");
            string args = $"-y -nostdin -hide_banner -loglevel error -nostats -i \"{video}\" -c:v {AppConfig.OutputCodec} -pix_fmt yuv420p -movflags +faststart \"{outputPath}\"";
            await RunFfmpegAsync(args, ct);
        }

        // ======== IMAGE PROCESSING ========
        private string ProcessImage(string inputFilePath, int imageIndex, string sessionFolder)
        {
            string outputImagePath = Path.Combine(sessionFolder, $"TEMP{imageIndex:D4}.jpg");
            Directory.CreateDirectory(sessionFolder);

            string filter = "[0:v]scale='if(gte(a,16/9),1800,-1)':'if(gte(a,16/9),-1,1012.50)'[fg];" +
                            "[0:v]scale=1920:1080,format=yuva420p,gblur=sigma=60[bg];" +
                            "[bg][fg]overlay=(W-w)/2:(H-h)/2,format=yuva420p";

            string args = $"-y -nostdin -hide_banner -loglevel error -nostats -i \"{inputFilePath}\" -filter_complex \"{filter}\" -q:v 1 -frames:v 1 \"{outputImagePath}\"";
            RunFfmpeg(args); // sync is fine per image
            return outputImagePath;
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

        private static string? PromptForAudioFile()
        {
            using var ofd = new OpenFileDialog();
            ofd.Title = "Select audio file (optional)";
            ofd.Filter = "Audio Files|*.mp3;*.wav;*.m4a;*.aac;*.flac;*.ogg|All Files|*.*";
            ofd.Multiselect = false;
            return ofd.ShowDialog() == DialogResult.OK ? ofd.FileName : null;
        }

        private static LoopPolicy GetLoopPolicy()
        {
            if (!AppConfig.PromptForLoopPolicy) return AppConfig.DefaultLoopPolicy;
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

        private static bool IsImageFile(string p)
        {
            string[] exts = { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".tif", ".webp", ".jfif" };
            return exts.Contains(Path.GetExtension(p).ToLowerInvariant());
        }
        private static bool IsVideoFile(string p)
        {
            string[] exts = { ".mp4", ".mov", ".mkv", ".avi", ".m4v" };
            return exts.Contains(Path.GetExtension(p).ToLowerInvariant());
        }
        private static bool IsAudioFile(string p)
        {
            string[] exts = { ".mp3", ".wav", ".m4a", ".aac", ".flac", ".ogg" };
            return exts.Contains(Path.GetExtension(p).ToLowerInvariant());
        }

        private static string? ChooseBestAudio(IEnumerable<string> audios)
        {
            if (audios == null) return null;
            var pick = audios
                .Where(a => !string.IsNullOrWhiteSpace(a) && File.Exists(a))
                .OrderByDescending(a => new FileInfo(a).Length)
                .FirstOrDefault();
            return string.IsNullOrWhiteSpace(pick) ? null : pick;
        }

        private static string EnsureDatedOutput()
        {
            string dated = Path.Combine(AppConfig.OutputRoot, DateTime.Now.ToString("MM-dd-yyyy"));
            Directory.CreateDirectory(dated);
            return dated;
        }

        private static string Sanitize(string name)
        {
            name = Regex.Replace(name, "[\\\\/:*?\"<>|]", "_");
            return name.Trim();
        }

        // ======== FFmpeg runners (fixed: drain stderr while running) ========
        private static void RunFfmpeg(string args)
        {
            using var p = new Process();
            p.StartInfo.FileName = AppConfig.FFmpegPath;
            p.StartInfo.Arguments = args;
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.RedirectStandardError = true;
            p.StartInfo.RedirectStandardOutput = false;
            p.StartInfo.CreateNoWindow = true;
            p.StartInfo.StandardErrorEncoding = Encoding.UTF8;

            var sb = new StringBuilder();
            p.ErrorDataReceived += (_, e) => { if (e.Data != null) sb.AppendLine(e.Data); };

            if (!p.Start()) throw new InvalidOperationException("Failed to start FFmpeg process.");

            p.BeginErrorReadLine();              // drain stderr to avoid deadlock
            p.WaitForExit();                     // sync wait is fine here
            p.CancelErrorRead();

            if (p.ExitCode != 0)
                throw new InvalidOperationException($"FFmpeg failed. Args: {args}\n{sb}");
        }

        private static async Task RunFfmpegAsync(string args, CancellationToken ct)
        {
            using var p = new Process();
            p.StartInfo.FileName = AppConfig.FFmpegPath;
            p.StartInfo.Arguments = args;
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.RedirectStandardError = true;
            p.StartInfo.RedirectStandardOutput = false;
            p.StartInfo.CreateNoWindow = true;
            p.StartInfo.StandardErrorEncoding = Encoding.UTF8;
            p.EnableRaisingEvents = true;

            var sb = new StringBuilder();
            p.ErrorDataReceived += (_, e) => { if (e.Data != null) sb.AppendLine(e.Data); };

            var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            p.Exited += (_, __) => tcs.TrySetResult(p.ExitCode);

            if (!p.Start()) throw new InvalidOperationException("Failed to start FFmpeg");

            p.BeginErrorReadLine(); // start draining BEFORE waiting

            using (ct.Register(() => { try { if (!p.HasExited) p.Kill(); } catch { } tcs.TrySetCanceled(ct); }))
            {
#if NET6_0_OR_GREATER
                try { await p.WaitForExitAsync(ct); }
                catch (OperationCanceledException) { /* ignore: already handled by Kill */ }
#else
                await Task.Run(() => p.WaitForExit(), ct);
#endif
                int code = p.HasExited ? p.ExitCode : await tcs.Task;
                p.CancelErrorRead();

                if (code != 0)
                    throw new InvalidOperationException($"FFmpeg failed. Args: {args}\n{sb}");
            }
        }

        private static void TryDeleteFile(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
        private static void TryDeleteFolder(string path)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { }
        }

        private static string Prompt(string text, string caption)
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
                Directory.CreateDirectory(AppConfig.OutputRoot);
                Process.Start("explorer.exe", AppConfig.OutputRoot);
                return;
            }

        }
    }
}
