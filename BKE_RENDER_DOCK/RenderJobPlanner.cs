using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace BKE_MediaTools
{
    internal enum DetectedAudioChoice
    {
        UseBest,
        Browse,
        Cancel
    }

    internal interface IRenderDockInteraction
    {
        Task<string?> PromptAsync(string text, string caption);
        Task<string?> PromptForAudioFileAsync();
        Task<LoopPolicy> GetLoopPolicyAsync();
        Task<DetectedAudioChoice> ChooseDetectedAudioAsync(IReadOnlyList<string> audios, string? bestAudio);
        Task<bool> OfferAudioFileAsync();
        Task ShowMessageAsync(string text, string caption);
    }

    internal static class RenderJobPlanner
    {
        public static async Task<IReadOnlyList<RenderJob>> PlanAsync(IEnumerable<string> droppedPaths, IRenderDockInteraction interaction)
        {
            var jobs = new List<RenderJob>();
            var images = new List<string>();
            var videos = new List<string>();
            var audios = new List<string>();

            foreach (var path in droppedPaths)
            {
                if (Directory.Exists(path))
                {
                    await PlanDirectoryAsync(path, jobs, interaction);
                }
                else if (File.Exists(path))
                {
                    if (RenderEngine.IsImageFile(path)) images.Add(path);
                    else if (RenderEngine.IsVideoFile(path)) videos.Add(path);
                    else if (RenderEngine.IsAudioFile(path)) audios.Add(path);
                }
            }

            await PlanLooseFilesAsync(images, videos, audios, jobs, interaction);
            return jobs;
        }

        private static async Task PlanDirectoryAsync(
            string path,
            List<RenderJob> jobs,
            IRenderDockInteraction interaction)
        {
            string folderName = Path.GetFileName(
                path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

            string stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string session = Path.Combine(
                RenderEngine.TempRoot,
                $"{stamp}_{RenderEngine.Sanitize(folderName)}");

            var files = Directory.GetFiles(path);
            var imgs = files.Where(RenderEngine.IsImageFile).OrderBy(p => p).ToList();
            var vids = files.Where(RenderEngine.IsVideoFile).OrderBy(p => p).ToList();
            var auds = files.Where(RenderEngine.IsAudioFile).OrderBy(p => p).ToList();

            if (imgs.Count == 0 && vids.Count == 0)
            {
                if (auds.Any())
                    await interaction.ShowMessageAsync(
                        $"“{folderName}” has only audio. Nothing to render.",
                        "Heads up");
                return;
            }

            Directory.CreateDirectory(session);

            if (imgs.Count > 0 && vids.Count > 0)
            {
                var processed = ProcessImages(imgs, session);
                jobs.Add(new RenderJob(
                    RenderJobType.SlideshowThenVideos,
                    folderName,
                    RenderEngine.EnsureDatedOutput(),
                    session,
                    processed,
                    RenderEngine.ChooseBestAudio(auds),
                    vids,
                    auds.Any() ? await interaction.GetLoopPolicyAsync() : null));
                return;
            }

            if (imgs.Count >= 1 && vids.Count == 0)
            {
                var processed = ProcessImages(imgs, session);
                jobs.Add(new RenderJob(
                    RenderJobType.SlideshowFromFolder,
                    folderName,
                    RenderEngine.EnsureDatedOutput(),
                    session,
                    processed,
                    auds.FirstOrDefault(),
                    null,
                    auds.Any() ? await interaction.GetLoopPolicyAsync() : null));
                return;
            }

            if (vids.Count >= 2)
            {
                string? audioPath = auds.FirstOrDefault();
                jobs.Add(new RenderJob(
                    RenderJobType.CombineVideos,
                    folderName + "_combined",
                    RenderEngine.EnsureDatedOutput(),
                    "",
                    vids,
                    audioPath,
                    null,
                    audioPath != null ? await interaction.GetLoopPolicyAsync() : null));
                return;
            }

            if (vids.Count == 1)
            {
                string? audioPath = auds.Count > 0
                    ? RenderEngine.ChooseBestAudio(auds)
                    : null;

                if (!string.IsNullOrEmpty(audioPath))
                {
                    jobs.Add(new RenderJob(
                        RenderJobType.AddAudioToVideo,
                        Path.GetFileNameWithoutExtension(vids[0]),
                        RenderEngine.EnsureDatedOutput(),
                        "",
                        new List<string> { vids[0] },
                        audioPath,
                        null,
                        await interaction.GetLoopPolicyAsync()));
                }
                else
                {
                    jobs.Add(new RenderJob(
                        RenderJobType.TranscodeSingleVideo,
                        Path.GetFileNameWithoutExtension(vids[0]),
                        RenderEngine.EnsureDatedOutput(),
                        "",
                        new List<string> { vids[0] }));
                }
            }
        }

        private static async Task PlanLooseFilesAsync(
            List<string> images,
            List<string> videos,
            List<string> audios,
            List<RenderJob> jobs,
            IRenderDockInteraction interaction)
        {
            images = images
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p)
                .ToList();
            videos = videos
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p)
                .ToList();
            audios = audios
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p)
                .ToList();

            if (images.Count > 0 && videos.Count > 0)
            {
                string? title = await interaction.PromptAsync(
                    "Enter the project title:",
                    "BKE MIX");

                if (!string.IsNullOrWhiteSpace(title))
                {
                    string stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                    string session = Path.Combine(
                        RenderEngine.TempRoot,
                        $"{stamp}_{RenderEngine.Sanitize(title)}");
                    Directory.CreateDirectory(session);

                    var processed = ProcessImages(images, session);

                    string? audioPath = null;
                    if (audios.Any())
                    {
                        var best = RenderEngine.ChooseBestAudio(audios);
                        var choice = await interaction.ChooseDetectedAudioAsync(audios, best);

                        if (choice == DetectedAudioChoice.UseBest)
                            audioPath = best;
                        else if (choice == DetectedAudioChoice.Browse)
                            audioPath = await interaction.PromptForAudioFileAsync();
                        else
                            return;
                    }
                    else if (RenderEngine.AlwaysPromptForAudioOnMixed &&
                             await interaction.OfferAudioFileAsync())
                    {
                        audioPath = await interaction.PromptForAudioFileAsync();
                    }

                    var policy = audioPath != null
                        ? await interaction.GetLoopPolicyAsync()
                        : (LoopPolicy?)null;

                    jobs.Add(new RenderJob(
                        RenderJobType.SlideshowThenVideos,
                        title,
                        RenderEngine.EnsureDatedOutput(),
                        session,
                        processed,
                        audioPath,
                        videos,
                        policy));
                }

                return;
            }

            if (images.Count == 1 && videos.Count == 0)
            {
                string? title = await interaction.PromptAsync(
                    "Enter the image name:",
                    "BKE Image");

                if (!string.IsNullOrWhiteSpace(title))
                {
                    string stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                    string session = Path.Combine(
                        RenderEngine.TempRoot,
                        $"{stamp}_{RenderEngine.Sanitize(title)}");
                    Directory.CreateDirectory(session);

                    jobs.Add(new RenderJob(
                        RenderJobType.SaveProcessedImage,
                        title,
                        RenderEngine.EnsureDatedOutput(),
                        session,
                        new List<string> { images[0] }));
                }

                return;
            }

            if (images.Count >= 2 && videos.Count == 0)
            {
                string? title = await interaction.PromptAsync(
                    "Enter the slideshow title:",
                    "BKE SLIDESHOW");

                if (!string.IsNullOrWhiteSpace(title))
                {
                    string stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                    string session = Path.Combine(
                        RenderEngine.TempRoot,
                        $"{stamp}_{RenderEngine.Sanitize(title)}");
                    Directory.CreateDirectory(session);

                    var processed = ProcessImages(images, session);

                    string? audioPath = audios.FirstOrDefault();
                    if (audioPath == null &&
                        RenderEngine.AlwaysPromptForAudioOnSlideshow)
                    {
                        audioPath = await interaction.PromptForAudioFileAsync();
                    }

                    jobs.Add(new RenderJob(
                        RenderJobType.SlideshowFromImages,
                        title,
                        RenderEngine.EnsureDatedOutput(),
                        session,
                        processed,
                        audioPath,
                        null,
                        audioPath != null
                            ? await interaction.GetLoopPolicyAsync()
                            : null));
                }

                return;
            }

            if (videos.Count >= 2 && images.Count == 0)
            {
                string outputName = await interaction.PromptAsync(
                    "Enter the combined video title:",
                    "CombinedVideo") ?? "CombinedVideo";

                string? audioPath = audios.FirstOrDefault();
                if (audioPath == null &&
                    RenderEngine.AlwaysPromptForAudioOnCombineVideos)
                {
                    audioPath = await interaction.PromptForAudioFileAsync();
                }

                jobs.Add(new RenderJob(
                    RenderJobType.CombineVideos,
                    outputName,
                    RenderEngine.EnsureDatedOutput(),
                    "",
                    videos,
                    audioPath,
                    null,
                    audioPath != null
                        ? await interaction.GetLoopPolicyAsync()
                        : null));
                return;
            }

            if (videos.Count == 1 && images.Count == 0)
            {
                string? audioPath = audios.FirstOrDefault();
                if (audioPath == null &&
                    RenderEngine.AlwaysPromptForAudioOnSingleVideo)
                {
                    audioPath = await interaction.PromptForAudioFileAsync();
                }

                if (!string.IsNullOrEmpty(audioPath))
                {
                    jobs.Add(new RenderJob(
                        RenderJobType.AddAudioToVideo,
                        Path.GetFileNameWithoutExtension(videos[0]),
                        RenderEngine.EnsureDatedOutput(),
                        "",
                        new List<string> { videos[0] },
                        audioPath,
                        null,
                        await interaction.GetLoopPolicyAsync()));
                }
                else
                {
                    jobs.Add(new RenderJob(
                        RenderJobType.TranscodeSingleVideo,
                        Path.GetFileNameWithoutExtension(videos[0]),
                        RenderEngine.EnsureDatedOutput(),
                        "",
                        new List<string> { videos[0] }));
                }

                return;
            }

            if (audios.Any())
            {
                await interaction.ShowMessageAsync(
                    "Only audio files were dropped. Nothing to render.",
                    "Heads up");
            }
        }

        private static List<string> ProcessImages(
            IEnumerable<string> images,
            string session)
        {
            var processed = new List<string>();
            int index = 0;

            foreach (var image in images)
            {
                processed.Add(RenderEngine.ProcessImage(
                    image,
                    ++index,
                    session));
            }

            return processed;
        }
    }
}
