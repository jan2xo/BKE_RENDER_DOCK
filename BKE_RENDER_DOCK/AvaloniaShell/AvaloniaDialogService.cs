using Avalonia;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using System.Linq;
using System.Threading.Tasks;

namespace BKE_MediaTools.AvaloniaShell
{
    internal enum DialogChoice
    {
        Primary,
        Secondary,
        Cancel
    }

    internal static class AvaloniaDialogService
    {
        internal static async Task ShowMessageAsync(
            Avalonia.Controls.Window owner,
            string title,
            string message)
        {
            var dialog = CreateDialog(title, 420, 210);
            var root = CreateRoot();
            root.Children.Add(CreateMessage(message));

            var ok = new Avalonia.Controls.Button
            {
                Content = "OK",
                MinWidth = 90,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right
            };
            ok.Click += (_, _) => dialog.Close();
            root.Children.Add(ok);

            dialog.Content = root;
            await dialog.ShowDialog(owner);
        }

        internal static async Task<bool> ConfirmAsync(
            Avalonia.Controls.Window owner,
            string title,
            string message,
            string acceptText,
            string cancelText)
        {
            var dialog = CreateDialog(title, 440, 220);
            var root = CreateRoot();
            root.Children.Add(CreateMessage(message));

            var buttons = CreateButtons();
            var accepted = false;

            var accept = new Avalonia.Controls.Button { Content = acceptText, MinWidth = 96 };
            var cancel = new Avalonia.Controls.Button { Content = cancelText, MinWidth = 96 };

            accept.Click += (_, _) => { accepted = true; dialog.Close(); };
            cancel.Click += (_, _) => dialog.Close();

            buttons.Children.Add(accept);
            buttons.Children.Add(cancel);
            root.Children.Add(buttons);

            dialog.Content = root;
            await dialog.ShowDialog(owner);
            return accepted;
        }

        internal static async Task<DialogChoice> ChooseAsync(
            Avalonia.Controls.Window owner,
            string title,
            string message,
            string primaryText,
            string secondaryText,
            string cancelText)
        {
            var dialog = CreateDialog(title, 520, 240);
            var root = CreateRoot();
            root.Children.Add(CreateMessage(message));

            var buttons = CreateButtons();
            var choice = DialogChoice.Cancel;

            var primary = new Avalonia.Controls.Button { Content = primaryText, MinWidth = 110 };
            var secondary = new Avalonia.Controls.Button { Content = secondaryText, MinWidth = 110 };
            var cancel = new Avalonia.Controls.Button { Content = cancelText, MinWidth = 96 };

            primary.Click += (_, _) => { choice = DialogChoice.Primary; dialog.Close(); };
            secondary.Click += (_, _) => { choice = DialogChoice.Secondary; dialog.Close(); };
            cancel.Click += (_, _) => dialog.Close();

            buttons.Children.Add(primary);
            buttons.Children.Add(secondary);
            buttons.Children.Add(cancel);
            root.Children.Add(buttons);

            dialog.Content = root;
            await dialog.ShowDialog(owner);
            return choice;
        }

        internal static async Task<string?> PromptAsync(
            Avalonia.Controls.Window owner,
            string title,
            string message)
        {
            var dialog = CreateDialog(title, 500, 250);
            var root = CreateRoot();
            root.Children.Add(CreateMessage(message));

            var input = new Avalonia.Controls.TextBox
            {
                MinWidth = 430,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
            };
            root.Children.Add(input);

            var buttons = CreateButtons();
            string? result = null;

            var ok = new Avalonia.Controls.Button { Content = "OK", MinWidth = 90 };
            var cancel = new Avalonia.Controls.Button { Content = "Cancel", MinWidth = 90 };

            ok.Click += (_, _) =>
            {
                result = input.Text;
                dialog.Close();
            };
            cancel.Click += (_, _) => dialog.Close();

            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            root.Children.Add(buttons);

            dialog.Content = root;
            await dialog.ShowDialog(owner);
            return result;
        }

        internal static async Task<string?> PickAudioFileAsync(Avalonia.Controls.Window owner)
        {
            var files = await owner.StorageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    Title = "Select audio file (optional)",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("Audio Files")
                        {
                            Patterns = new[] { "*.mp3", "*.wav", "*.m4a", "*.aac", "*.flac", "*.ogg" }
                        }
                    }
                });

            return files.FirstOrDefault()?.Path.LocalPath;
        }

        private static Avalonia.Controls.Window CreateDialog(
            string title,
            double width,
            double height) =>
            new()
            {
                Title = title,
                Width = width,
                Height = height,
                CanResize = false,
                WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false
            };

        private static Avalonia.Controls.StackPanel CreateRoot() =>
            new()
            {
                Margin = new Thickness(20),
                Spacing = 16
            };

        private static Avalonia.Controls.StackPanel CreateButtons() =>
            new()
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                Spacing = 8
            };

        private static Avalonia.Controls.TextBlock CreateMessage(string message) =>
            new()
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 470
            };
    }
}
