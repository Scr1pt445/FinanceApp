using System.ComponentModel;
using FinanceApp.Presentation.Settings;

namespace FinanceApp.Mobile.Controls;

/// <summary>
/// Four dots and a round-key keypad, bound to a <see cref="PinPadViewModel"/>. The bottom-left key is the
/// fingerprint when <see cref="BiometricRequested"/> has a handler, otherwise empty. A wrong PIN shakes the dots.
/// </summary>
public sealed class PinPadView : ContentView
{
    private const double Key = 76;

    private readonly HorizontalStackLayout _dots = new() { Spacing = 18, HorizontalOptions = LayoutOptions.Center };
    private readonly Label _message = new() { FontSize = 14, TextColor = Palette.MutedText, HorizontalTextAlignment = TextAlignment.Center, MinimumHeightRequest = 40 };
    private readonly Button _bio;
    private PinPadViewModel? _pad;

    public PinPadView()
    {
        for (var i = 0; i < Application.Settings.PinCode.Length; i++)
        {
            _dots.Add(new BoxView { WidthRequest = 16, HeightRequest = 16, CornerRadius = 8, Color = Palette.Border });
        }

        var keys = new Grid { RowSpacing = 14, ColumnSpacing = 22, HorizontalOptions = LayoutOptions.Center };
        for (var c = 0; c < 3; c++)
        {
            keys.ColumnDefinitions.Add(new ColumnDefinition(Key));
        }

        for (var r = 0; r < 4; r++)
        {
            keys.RowDefinitions.Add(new RowDefinition(Key));
        }

        for (var digit = 1; digit <= 9; digit++)
        {
            keys.Add(Digit(digit.ToString(System.Globalization.CultureInfo.InvariantCulture)), (digit - 1) % 3, (digit - 1) / 3);
        }

        _bio = Round("☝", 30, Colors.Transparent);
        _bio.TextColor = Palette.AccentLight;
        _bio.IsVisible = false;
        _bio.Clicked += (_, _) => BiometricRequested?.Invoke(this, EventArgs.Empty);
        keys.Add(_bio, 0, 3);
        keys.Add(Digit("0"), 1, 3);
        var erase = Round("⌫", 24, Colors.Transparent);
        erase.Clicked += (_, _) => _pad?.EraseCommand.Execute(null);
        keys.Add(erase, 2, 3);

        Content = new VerticalStackLayout { Spacing = 20, Children = { _dots, _message, keys } };
    }

    /// <summary>The fingerprint key was pressed.</summary>
    public event EventHandler? BiometricRequested;

    public bool ShowBiometric
    {
        get => _bio.IsVisible;
        set => _bio.IsVisible = value;
    }

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        if (_pad is not null)
        {
            _pad.PropertyChanged -= OnPadChanged;
        }

        _pad = BindingContext as PinPadViewModel;
        if (_pad is not null)
        {
            _pad.PropertyChanged += OnPadChanged;
        }

        Render();
    }

    private static Button Round(string text, double size, Color background) => new()
    {
        Text = text,
        FontSize = size,
        FontAttributes = FontAttributes.None,
        TextColor = Palette.Text,
        BackgroundColor = background,
        CornerRadius = (int)(Key / 2),
        WidthRequest = Key,
        HeightRequest = Key,
        Padding = 0,
    };

    private Button Digit(string digit)
    {
        var button = Round(digit, 28, Palette.Control);
        button.Clicked += (_, _) => _pad?.PressCommand.Execute(digit);
        return button;
    }

    private async void OnPadChanged(object? sender, PropertyChangedEventArgs e)
    {
        Render();
        if (e.PropertyName == nameof(PinPadViewModel.IsWrong) && _pad?.IsWrong == true)
        {
            try
            {
                for (var i = 0; i < 3; i++)
                {
                    await _dots.TranslateToAsync(-12, 0, 50);
                    await _dots.TranslateToAsync(12, 0, 50);
                }

                await _dots.TranslateToAsync(0, 0, 50);
            }
#pragma warning disable CA1031 // Only an animation.
            catch (Exception)
#pragma warning restore CA1031
            {
            }
        }
    }

    private void Render()
    {
        if (_pad is null)
        {
            return;
        }

        var dots = _pad.Dots;
        for (var i = 0; i < _dots.Count; i++)
        {
            ((BoxView)_dots[i]).Color = _pad.IsWrong ? Palette.Warning : dots[i] ? Palette.Accent : Palette.Border;
        }

        _message.Text = _pad.Message;
        _message.TextColor = _pad.IsWrong ? Palette.Warning : Palette.MutedText;
    }
}
