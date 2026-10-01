using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace BKE_MediaTools
{
    internal enum RenderJobType
    {
        SlideshowFromFolder,
        SlideshowFromImages,
        CombineVideos,
        AddAudioToVideo,
        TranscodeSingleVideo,
        SlideshowThenVideos,
        SaveProcessedImage
    }

    internal enum LoopPolicy
    {
        Shortest,
        LoopVideoToAudio,
        LoopAudioToVideo
    }

    internal record RenderJob(
        RenderJobType Type,
        string Title,
        string OutputFolder,
        string SessionFolder,
        List<string> Inputs,
        string? AudioPath = null,
        List<string>? ExtraVideos = null,
        LoopPolicy? LoopMode = null);

    internal static class RenderEngine
    {
        private static class AppConfig
        {
            public static readonly string FFmpegPath = @"C:\ffmpeg\bin\ffmpeg.exe";
            public static readonly string OutputRoot;
            public static readonly string TempRoot;

            public const int Fps = 30;
            public const int SecondsPerImage = 3;
            public const string OutputCodec = "h264_nvenc";
            public static readonly LoopPolicy DefaultLoopPolicy = LoopPolicy.Shortest;

            private static string SelectRoot()
            {
                var d = DriveInfo.GetDrives().FirstOrDefault(dr =>
                    dr.IsReady &&
                    dr.DriveType == DriveType.Fixed &&
                    dr.Name.StartsWith("D:", StringComparison.OrdinalIgnoreCase));
                return d?.Name ?? @"C:\";
            }

            private static void EnsurePathsOrElevate()
            {
                try
                {
                    Directory.CreateDirectory(OutputRoot);
                    Directory.CreateDirectory(TempRoot);
                }
                catch (UnauthorizedAccessException)
                {
                    if (!IsAdministrator())
                    {
                        RelaunchAsAdministrator();
                        Environment.Exit(0);
                    }
                    else
                    {
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
                    Verb = "runas",
                    UseShellExecute = true,
                    WorkingDirectory = Environment.CurrentDirectory
                };

                try { Process.Start(psi); }
                catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
                {
                    // User canceled UAC.
                }
            }

            private static string Quote(string s) => s.Contains(' ') ? $"\"{s}\"" : s;

            static AppConfig()
            {
                string root = SelectRoot();
                OutputRoot = Path.Combine(root, "BKE_RENDER_DOCK");
                TempRoot = Path.Combine(OutputRoot, "TEMP");
                EnsurePathsOrElevate();
            }
        }

        public static string OutputRoot => AppConfig.OutputRoot;
        public static string TempRoot => AppConfig.TempRoot;
        public static int Fps => AppConfig.Fps;
        public static int SecondsPerImage => AppConfig.SecondsPerImage;
        public static bool AlwaysPromptForAudioOnSingleVideo => true;
        public static bool AlwaysPromptForAudioOnSlideshow => true;
        public static bool AlwaysPromptForAudioOnCombineVideos => true;
        public static bool AlwaysPromptForAudioOnMixed => true;
        public static bool PromptForLoopPolicy => true;
        public static LoopPolicy DefaultLoopPolicy => AppConfig.DefaultLoopPolicy;

        public static string EnsureDatedOutput()
        {
            string dated = Path.Combine(OutputRoot, DateTime.Now.ToString("MM-dd-yyyy"));
            Directory.CreateDirectory(dated);
            return dated;
        }

        public static string Sanitize(string name)
        {
            name = Regex.Replace(name, "[\\\\/:*?\"<>|]", "_");
            return name.Trim();
        }

        public static bool IsImageFile(string p)
        {
            string[] exts = { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".tif", ".webp", ".jfif" };
            return exts.Contains(Path.GetExtension(p).ToLowerInvariant());
        }

        public static bool IsVideoFile(string p)
        {
            string[] exts = { ".mp4", ".mov", ".mkv", ".avi", ".m4v" };
            return exts.Contains(Path.GetExtension(p).ToLowerInvariant());
        }

        public static bool IsAudioFile(string p)
        {
            string[] exts = { ".mp3", ".wav", ".m4a", ".aac", ".flac", ".ogg" };
            return exts.Contains(Path.GetExtension(p).ToLowerInvariant());
        }

        public static string? ChooseBestAudio(IEnumerable<string> audios)
        {
            if (audios == null) return null;
            var pick = audios
                .Where(a => !string.IsNullOrWhiteSpace(a) && File.Exists(a))
                .OrderByDescending(a => new FileInfo(a).Length)
                .FirstOrDefault();
            return string.IsNullOrWhiteSpace(pick) ? null : pick;
        }

        public static string ProcessImage(string inputFilePath, int imageIndex, string sessionFolder)
        {
            string outputImagePath = Path.Combine(sessionFolder, $"TEMP{imageIndex:D4}.jpg");
            Directory.CreateDirectory(sessionFolder);

            string filter = "[0:v]scale='if(gte(a,16/9),1800,-1)':'if(gte(a,16/9),-1,1012.50)'[fg];" +
                            "[0:v]scale=1920:1080,format=yuva420p,gblur=sigma=60[bg];" +
                            "[bg][fg]overlay=(W-w)/2:(H-h)/2,format=yuva420p";

            string args = $"-y -nostdin -hide_banner -loglevel error -nostats -i \"{inputFilePath}\" -filter_complex \"{filter}\" -q:v 1 -frames:v 1 \"{outputImagePath}\"";
            RunFfmpeg(args);
            return outputImagePath;
        }

        public static async Task ExecuteAsync(RenderJob job, CancellationToken ct)
        {
            switch (job.Type)
            {
                case RenderJobType.SlideshowFromFolder:
                case RenderJobType.SlideshowFromImages:
                    await BuildSlideshowAsync(job, ct);
                    break;
                case RenderJobType.CombineVideos:
                    await CombineVideosAsync(job, ct);
                    break;
                case RenderJobType.SlideshowThenVideos:
                    await BuildSlideshowThenVideosAsync(job, ct);
                    break;
                case RenderJobType.SaveProcessedImage:
                    await SaveProcessedImageAsync(job, ct);
                    break;
                case RenderJobType.AddAudioToVideo:
                    await AddAudioToVideoAsync(job, ct);
                    break;
                case RenderJobType.TranscodeSingleVideo:
                    await TranscodeSingleVideoAsync(job, ct);
                    break;
            }
        }

        private static async Task SaveProcessedImageAsync(RenderJob job, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            string src = job.Inputs[0];
            string processed = ProcessImage(src, 1, job.SessionFolder);
            string outPath = Path.Combine(job.OutputFolder, $"{Sanitize(job.Title)}.jpg");
            TryDeleteFile(outPath);
            File.Move(processed, outPath);
            TryDeleteFolder(job.SessionFolder);
            await Task.CompletedTask;
        }

        private static async Task BuildSlideshowAsync(RenderJob job, CancellationToken ct)
        {
            if (job.Inputs.Count == 0) return;

            string tempSlide = Path.Combine(job.SessionFolder, "_slideshow.mp4");
            string pattern = Path.Combine(job.SessionFolder, "TEMP%04d.jpg");
            int totalSeconds = job.Inputs.Count * SecondsPerImage;
            string vf = $"scale=8000:-1,setsar=1,zoompan=z='zoom+0.001':x=iw/2-(iw/zoom/2):y=ih/2-(ih/zoom/2):d={Fps * SecondsPerImage}:s=1920x1080:fps={Fps}";
            string slideArgs = $"-y -nostdin -hide_banner -loglevel error -nostats -framerate {Fps} -i \"{pattern}\" -vf \"{vf}\" -t {totalSeconds} -c:v {AppConfig.OutputCodec} -pix_fmt yuv420p -max_muxing_queue_size 2048 \"{tempSlide}\"";
            await RunFfmpegAsync(slideArgs, ct);

            string outputPath = Path.Combine(job.OutputFolder, $"{Sanitize(job.Title)}.mp4");

            if (!string.IsNullOrWhiteSpace(job.AudioPath))
            {
                var mode = job.LoopMode ?? DefaultLoopPolicy;
                string args = mode switch
                {
                    LoopPolicy.LoopVideoToAudio => $"-y -nostdin -hide_banner -loglevel error -nostats -stream_loop -1 -i \"{tempSlide}\" -i \"{job.AudioPath}\" -shortest -map 0:v -map 1:a -c:v {AppConfig.OutputCodec} -pix_fmt yuv420p -c:a aac -b:a 192k -ar 48000 -movflags +faststart \"{outputPath}\"",
                    LoopPolicy.LoopAudioToVideo => $"-y -nostdin -hide_banner -loglevel error -nostats -i \"{tempSlide}\" -stream_loop -1 -i \"{job.AudioPath}\" -shortest -map 0:v -map 1:a -c:v copy -c:a aac -b:a 192k -ar 48000 -movflags +faststart \"{outputPath}\"",
                    _ => $"-y -nostdin -hide_banner -loglevel error -nostats -i \"{tempSlide}\" -i \"{job.AudioPath}\" -shortest -map 0:v -map 1:a -c:v copy -c:a aac -b:a 192k -ar 48000 -movflags +faststart \"{outputPath}\""
                };
                await RunFfmpegAsync(args, ct);
            }
            else
            {
                TryDeleteFile(outputPath);
                File.Move(tempSlide, outputPath);
            }

            TryDeleteFolder(job.SessionFolder);
        }

        private static async Task BuildSlideshowThenVideosAsync(RenderJob job, CancellationToken ct)
        {
            if (job.Inputs.Count == 0 || job.ExtraVideos == null || job.ExtraVideos.Count == 0) return;

            string slideshowPath = Path.Combine(job.SessionFolder, "_slideshow.mp4");
            int totalSeconds = job.Inputs.Count * SecondsPerImage;
            string pattern = Path.Combine(job.SessionFolder, "TEMP%04d.jpg");
            string vf = $"scale=8000:-1,setsar=1,zoompan=z='zoom+0.001':x=iw/2-(iw/zoom/2):y=ih/2-(ih/zoom/2):d={Fps * SecondsPerImage}:s=1920x1080:fps={Fps}";
            string slideArgs = $"-y -nostdin -hide_banner -loglevel error -nostats -framerate {Fps} -i \"{pattern}\" -vf \"{vf}\" -t {totalSeconds} -c:v {AppConfig.OutputCodec} -pix_fmt yuv420p -max_muxing_queue_size 2048 \"{slideshowPath}\"";
            await RunFfmpegAsync(slideArgs, ct);

            var normalized = new List<string> { slideshowPath };
            int idx = 0;
            foreach (var v in job.ExtraVideos)
            {
                ct.ThrowIfCancellationRequested();
                string outVid = Path.Combine(job.SessionFolder, $"_norm_{idx++:000}.mp4");
                string normArgs = $"-y -nostdin -hide_banner -loglevel error -nostats -i \"{v}\" -vf scale=1920:1080:force_original_aspect_ratio=decrease,pad=1920:1080:(ow-iw)/2:(oh-ih)/2,fps={Fps} -c:v {AppConfig.OutputCodec} -pix_fmt yuv420p -vsync cfr -an -max_muxing_queue_size 2048 \"{outVid}\"";
                await RunFfmpegAsync(normArgs, ct);
                normalized.Add(outVid);
            }

            string listPath = Path.Combine(job.SessionFolder, "_concat.txt");
            File.WriteAllLines(listPath, normalized.Select(p => $"file '{p.Replace("'", "'\\''")}'"));

            string joinedPath = Path.Combine(job.SessionFolder, "_joined.mp4");
            string concatArgs = $"-y -nostdin -hide_banner -loglevel error -nostats -f concat -safe 0 -i \"{listPath}\" -c copy -an \"{joinedPath}\"";
            await RunFfmpegAsync(concatArgs, ct);

            string outputPath = Path.Combine(job.OutputFolder, $"{Sanitize(job.Title)}.mp4");

            if (!string.IsNullOrWhiteSpace(job.AudioPath))
            {
                var mode = job.LoopMode ?? DefaultLoopPolicy;
                string finalArgs = mode switch
                {
                    LoopPolicy.LoopVideoToAudio => $"-y -nostdin -hide_banner -loglevel error -nostats -stream_loop -1 -i \"{joinedPath}\" -i \"{job.AudioPath}\" -shortest -map 0:v -map 1:a -c:v {AppConfig.OutputCodec} -pix_fmt yuv420p -c:a aac -b:a 192k -ar 48000 -movflags +faststart \"{outputPath}\"",
                    LoopPolicy.LoopAudioToVideo => $"-y -nostdin -hide_banner -loglevel error -nostats -i \"{joinedPath}\" -stream_loop -1 -i \"{job.AudioPath}\" -shortest -map 0:v -map 1:a -c:v copy -c:a aac -b:a 192k -ar 48000 -movflags +faststart \"{outputPath}\"",
                    _ => $"-y -nostdin -hide_banner -loglevel error -nostats -i \"{joinedPath}\" -i \"{job.AudioPath}\" -shortest -map 0:v -map 1:a -c:v copy -c:a aac -b:a 192k -ar 48000 -movflags +faststart \"{outputPath}\""
                };
                await RunFfmpegAsync(finalArgs, ct);
            }
            else
            {
                TryDeleteFile(outputPath);
                File.Move(joinedPath, outputPath);
            }

            TryDeleteFolder(job.SessionFolder);
        }

        private static async Task CombineVideosAsync(RenderJob job, CancellationToken ct)
        {
            string session = string.IsNullOrWhiteSpace(job.SessionFolder)
                ? Path.Combine(TempRoot, $"comb_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}")
                : job.SessionFolder;
            Directory.CreateDirectory(session);

            var normalized = new List<string>();
            int idx = 0;
            foreach (var v in job.Inputs)
            {
                ct.ThrowIfCancellationRequested();
                string outVid = Path.Combine(session, $"_norm_{idx++:000}.mp4");
                string normArgs =
                    $"-y -nostdin -hide_banner -loglevel error -nostats -i \"{v}\" " +
                    $"-vf scale=1920:1080:force_original_aspect_ratio=decrease,pad=1920:1080:(ow-iw)/2:(oh-ih)/2,fps={Fps} " +
                    $"-c:v {AppConfig.OutputCodec} -pix_fmt yuv420p -vsync cfr -an " +
                    $"-max_muxing_queue_size 2048 \"{outVid}\"";
                await RunFfmpegAsync(normArgs, ct);
                normalized.Add(outVid);
            }

            string listPath = Path.Combine(session, "_concat.txt");
            File.WriteAllLines(listPath, normalized.Select(p => $"file '{p.Replace("'", "'\\''")}'"));

            string joinedPath = Path.Combine(session, "_joined.mp4");
            string concatArgs =
                $"-y -nostdin -hide_banner -loglevel error -nostats " +
                $"-f concat -safe 0 -i \"{listPath}\" -c copy -an \"{joinedPath}\"";
            await RunFfmpegAsync(concatArgs, ct);

            string outputPath = Path.Combine(job.OutputFolder, $"{Sanitize(job.Title)}_{DateTime.Now:yyyyMMddHHmmss}.mp4");
            if (!string.IsNullOrWhiteSpace(job.AudioPath))
            {
                var mode = job.LoopMode ?? DefaultLoopPolicy;
                string finalArgs = mode switch
                {
                    LoopPolicy.LoopVideoToAudio =>
                        $"-y -nostdin -hide_banner -loglevel error -nostats -stream_loop -1 -i \"{joinedPath}\" -i \"{job.AudioPath}\" -shortest -map 0:v -map 1:a -c:v {AppConfig.OutputCodec} -pix_fmt yuv420p -c:a aac -b:a 192k -ar 48000 -movflags +faststart \"{outputPath}\"",
                    LoopPolicy.LoopAudioToVideo =>
                        $"-y -nostdin -hide_banner -loglevel error -nostats -i \"{joinedPath}\" -stream_loop -1 -i \"{job.AudioPath}\" -shortest -map 0:v -map 1:a -c:v copy -c:a aac -b:a 192k -ar 48000 -movflags +faststart \"{outputPath}\"",
                    _ =>
                        $"-y -nostdin -hide_banner -loglevel error -nostats -i \"{joinedPath}\" -i \"{job.AudioPath}\" -shortest -map 0:v -map 1:a -c:v copy -c:a aac -b:a 192k -ar 48000 -movflags +faststart \"{outputPath}\""
                };
                await RunFfmpegAsync(finalArgs, ct);
            }
            else
            {
                TryDeleteFile(outputPath);
                File.Move(joinedPath, outputPath);
            }

            TryDeleteFile(listPath);
            TryDeleteFolder(session);
        }

        private static async Task AddAudioToVideoAsync(RenderJob job, CancellationToken ct)
        {
            string video = job.Inputs[0];
            string audio = job.AudioPath!;
            var mode = job.LoopMode ?? DefaultLoopPolicy;
            string outputPath = Path.Combine(job.OutputFolder, $"{Sanitize(Path.GetFileNameWithoutExtension(video))}_withAudio.mp4");

            string args = mode switch
            {
                LoopPolicy.LoopVideoToAudio =>
                    $"-y -nostdin -hide_banner -loglevel error -nostats -stream_loop -1 -i \"{video}\" -i \"{audio}\" -shortest -map 0:v -map 1:a -c:v {AppConfig.OutputCodec} -pix_fmt yuv420p -c:a aac -b:a 192k -ar 48000 -movflags +faststart \"{outputPath}\"",
                LoopPolicy.LoopAudioToVideo =>
                    $"-y -nostdin -hide_banner -loglevel error -nostats -i \"{video}\" -stream_loop -1 -i \"{audio}\" -shortest -map 0:v -map 1:a -c:v {AppConfig.OutputCodec} -pix_fmt yuv420p -c:a aac -b:a 192k -ar 48000 -movflags +faststart \"{outputPath}\"",
                _ =>
                    $"-y -nostdin -hide_banner -loglevel error -nostats -i \"{video}\" -i \"{audio}\" -shortest -map 0:v -map 1:a -c:v {AppConfig.OutputCodec} -pix_fmt yuv420p -c:a aac -b:a 192k -ar 48000 -movflags +faststart \"{outputPath}\""
            };

            await RunFfmpegAsync(args, ct);
        }

        private static async Task TranscodeSingleVideoAsync(RenderJob job, CancellationToken ct)
        {
            string video = job.Inputs[0];
            string outputPath = Path.Combine(job.OutputFolder, $"{Sanitize(Path.GetFileNameWithoutExtension(video))}.mp4");
            string args = $"-y -nostdin -hide_banner -loglevel error -nostats -i \"{video}\" -c:v {AppConfig.OutputCodec} -pix_fmt yuv420p -movflags +faststart \"{outputPath}\"";
            await RunFfmpegAsync(args, ct);
        }

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
            p.BeginErrorReadLine();
            p.WaitForExit();
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
            p.BeginErrorReadLine();

            using (ct.Register(() => { try { if (!p.HasExited) p.Kill(); } catch { } }))
            {
                try
                {
                    await p.WaitForExitAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }

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
    }
}
