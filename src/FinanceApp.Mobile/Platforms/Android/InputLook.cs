using Android.Content.Res;
using Microsoft.Maui.Handlers;

namespace FinanceApp.Mobile;

/// <summary>
/// Android draws a material underline under every text field; inside a <see cref="Controls.Field"/> plate it is
/// a second border, so it is tinted away for all the inputs the app uses.
/// </summary>
internal static class InputLook
{
    public static void Apply()
    {
        var transparent = ColorStateList.ValueOf(Android.Graphics.Color.Transparent);
        EntryHandler.Mapper.AppendToMapping(nameof(InputLook), (handler, _) => handler.PlatformView.BackgroundTintList = transparent);
        EditorHandler.Mapper.AppendToMapping(nameof(InputLook), (handler, _) => handler.PlatformView.BackgroundTintList = transparent);
        PickerHandler.Mapper.AppendToMapping(nameof(InputLook), (handler, _) => handler.PlatformView.BackgroundTintList = transparent);
        DatePickerHandler.Mapper.AppendToMapping(nameof(InputLook), (handler, _) => handler.PlatformView.BackgroundTintList = transparent);
    }
}
