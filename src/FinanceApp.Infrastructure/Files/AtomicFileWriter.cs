using FinanceApp.Application.Abstractions;

namespace FinanceApp.Infrastructure.Files;

/// <summary>
/// Writes a temporary file next to the target and swaps it in with <see cref="File.Replace(string, string, string?)"/>,
/// so readers (Obsidian, sync clients) see either the old or the new file, never a partial one.
/// </summary>
public sealed class AtomicFileWriter : IFileWriter
{
    public async Task WriteAtomicallyAsync(string path, ReadOnlyMemory<byte> content, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var target = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(target)!;
        Directory.CreateDirectory(directory);

        // Same folder => same volume, so the swap is a rename, not a copy.
        var temp = Path.Combine(directory, $".{Path.GetFileName(target)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
            {
                await stream.WriteAsync(content, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(target))
            {
                File.Replace(temp, target, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temp, target);
            }
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }
}
