namespace FinanceApp.Mobile.Controls;

/// <summary>
/// The frame around every input: a rounded plate that lights up in the accent while its entry, picker or date
/// picker has focus. The inputs themselves are transparent and, on Android, lose their underline (InputLook).
/// </summary>
public sealed class Field : Border
{
    public Field()
    {
        DescendantAdded += (_, e) =>
        {
            if (e.Element is InputView or Picker or DatePicker)
            {
                var input = (VisualElement)e.Element;
                input.Focused += (_, _) => Stroke = Palette.Accent;
                input.Unfocused += (_, _) => Stroke = Palette.Border;
            }
        };
    }
}
