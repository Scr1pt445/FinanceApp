using FinanceApp.Presentation.Data;

namespace FinanceApp.Mobile;

/// <summary>Files go out through the Android share sheet: mail, messengers, cloud drives.</summary>
public sealed class MauiFileSharer : IFileSharer
{
    // The cache folder: shared files are copies, Android may clear it when space runs low.
    public string ShareFolder => Path.Combine(FileSystem.CacheDirectory, "share");

    public Task ShareAsync(string title, IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return Share.Default.RequestAsync(new ShareMultipleFilesRequest
        {
            Title = title,
            Files = paths.Select(p => new ShareFile(p)).ToList(),
        });
    }
}
