using System.Globalization;

namespace FinanceApp.Mobile;

/// <summary>
/// A phone has no console: when the app dies, the reason goes to a file, and the next start shows it instead of
/// the home screen, so it can be read or copied without a computer and adb.
/// </summary>
internal static class CrashReport
{
    private static string FilePath => Path.Combine(FileSystem.AppDataDirectory, "last-crash.txt");

    public static void Save(Exception exception)
    {
        try
        {
            File.WriteAllText(FilePath, string.Create(CultureInfo.InvariantCulture, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}{exception}"));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>The last saved report, removed so it is shown once; <c>null</c> if the last run ended normally.</summary>
    public static string? TakeLast()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return null;
            }

            var text = File.ReadAllText(FilePath);
            File.Delete(FilePath);
            return text;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>Shown on the start after a crash: what happened, as selectable text.</summary>
internal sealed class CrashApp(string report) : Microsoft.Maui.Controls.Application
{
    protected override Window CreateWindow(IActivationState? activationState)
    {
        UserAppTheme = AppTheme.Dark;
        var text = new Editor
        {
            Text = report,
            IsReadOnly = true,
            FontSize = 12,
            TextColor = Color.FromArgb("#F2F2F2"),
            BackgroundColor = Color.FromArgb("#141414"),
            AutoSize = EditorAutoSizeOption.TextChanges,
        };
        var page = new ContentPage
        {
            BackgroundColor = Color.FromArgb("#0B0B0B"),
            Padding = new Thickness(16),
            Content = new ScrollView
            {
                Content = new VerticalStackLayout
                {
                    Spacing = 12,
                    Children =
                    {
                        new Label
                        {
                            Text = "Прошлый запуск FinanceApp завершился ошибкой. Скопируйте или сделайте снимок текста ниже "
                                + "и пришлите разработчику. Чтобы продолжить, закройте приложение и откройте снова.",
                            TextColor = Color.FromArgb("#F5A35C"),
                            FontSize = 15,
                        },
                        text,
                    },
                },
            },
        };
        return new Window(page) { Title = "FinanceApp" };
    }
}
