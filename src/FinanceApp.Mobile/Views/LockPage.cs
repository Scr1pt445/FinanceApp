using FinanceApp.Mobile.Controls;
using FinanceApp.Presentation.Settings;

namespace FinanceApp.Mobile.Views;

/// <summary>
/// Shown over everything while the app is locked: on start and after a while in the background. A fingerprint is
/// asked for at once when allowed; the PIN pad is always there. Back leaves the app instead of the lock.
/// </summary>
public sealed class LockPage : ContentPage
{
    private readonly LockViewModel _lock;
    private readonly IBiometrics _biometrics;
    private bool _asking;

    public LockPage(LockViewModel lockViewModel, IBiometrics biometrics)
    {
        ArgumentNullException.ThrowIfNull(lockViewModel);
        _lock = lockViewModel;
        _biometrics = biometrics;
        BackgroundColor = Palette.Window;
        lockViewModel.Reset();

        var pad = new PinPadView { BindingContext = lockViewModel, ShowBiometric = lockViewModel.BiometricsOn && biometrics.IsAvailable };
        pad.BiometricRequested += async (_, _) => await AskBiometricsAsync();
        Content = new Grid
        {
            RowDefinitions = { new(GridLength.Star), new(GridLength.Auto), new(GridLength.Star) },
            Padding = new Thickness(24),
            Children =
            {
                new VerticalStackLayout
                {
                    Spacing = 10,
                    VerticalOptions = LayoutOptions.End,
                    Margin = new Thickness(0, 0, 0, 28),
                    Children =
                    {
                        new Border
                        {
                            WidthRequest = 72,
                            HeightRequest = 72,
                            StrokeThickness = 0,
                            BackgroundColor = Palette.AccentSoft,
                            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 36 },
                            HorizontalOptions = LayoutOptions.Center,
                            Content = new Label { Text = "🔒", FontSize = 30, HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center },
                        },
                        new Label { Text = "FinanceApp", FontSize = 24, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center },
                    },
                },
            },
        };
        ((Grid)Content).Add(pad, 0, 1);
    }

    public LockViewModel Lock => _lock;

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await AskBiometricsAsync();
    }

    /// <summary>Back on the lock screen leaves the app (to the home screen), it does not unlock it.</summary>
    protected override bool OnBackButtonPressed()
    {
#if ANDROID
        Platform.CurrentActivity?.MoveTaskToBack(true);
#endif
        return true;
    }

    private async Task AskBiometricsAsync()
    {
        if (_asking || !_lock.BiometricsOn || !_biometrics.IsAvailable)
        {
            return;
        }

        _asking = true;
        try
        {
            if (await _biometrics.AuthenticateAsync("Вход в FinanceApp"))
            {
                _lock.UnlockByBiometrics();
            }
        }
#pragma warning disable CA1031 // The PIN pad stays; the fingerprint is a shortcut.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            CrashReport.Save(ex);
        }
        finally
        {
            _asking = false;
        }
    }
}
