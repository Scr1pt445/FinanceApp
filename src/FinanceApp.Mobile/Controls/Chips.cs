namespace FinanceApp.Mobile.Controls;

/// <summary>
/// A row of chip buttons, one per option: the chosen one is filled with the accent. Used instead of system pickers
/// where there are only a few options — one tap, and nothing for Android to reset.
/// </summary>
public static class Chips
{
    public static void Highlight(Layout row, object? chosen)
    {
        ArgumentNullException.ThrowIfNull(row);
        foreach (var button in row.Children.OfType<Button>())
        {
            var selected = Equals(button.BindingContext, chosen);
            button.BackgroundColor = selected ? Palette.Accent : Palette.Control;
            button.TextColor = selected ? Colors.White : Palette.Text;
        }
    }
}
