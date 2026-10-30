using FinanceApp.Presentation.Data;

namespace FinanceApp.Mobile;

/// <summary>
/// Android's file chooser: Downloads, a cloud drive, a file received in a messenger. The chosen file is copied into
/// the app's cache first — a content URI is not a path SQLite can open.
/// </summary>
public sealed class MauiFileChooser : IFileChooser
{
    public async Task<string?> PickAsync(string title)
    {
        var picked = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = title });
        if (picked is null)
        {
            return null;
        }

        var folder = Path.Combine(FileSystem.CacheDirectory, "restore");
        Directory.CreateDirectory(folder);
        var local = Path.Combine(folder, Path.GetFileName(picked.FileName));
        await using (var source = await picked.OpenReadAsync())
        await using (var target = File.Create(local))
        {
            await source.CopyToAsync(target);
        }

        return local;
    }
}
