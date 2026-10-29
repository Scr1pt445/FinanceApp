using System.Text;
using FinanceApp.Infrastructure.Files;

namespace FinanceApp.Infrastructure.Tests.Files;

public sealed class AtomicFileWriterTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "FinanceApp.Tests", Guid.NewGuid().ToString("N"));
    private readonly AtomicFileWriter _writer = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string Target => Path.Combine(_directory, "Data", "transactions.csv");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task CreatesFileAndFolder_ExactBytes()
    {
        await _writer.WriteAtomicallyAsync(Target, "date,type\n"u8.ToArray(), Ct);

        (await File.ReadAllBytesAsync(Target, Ct)).ShouldBe("date,type\n"u8.ToArray());
    }

    [Fact]
    public async Task ReplacesExistingFile_LeavesNoTempFiles()
    {
        await _writer.WriteAtomicallyAsync(Target, Encoding.UTF8.GetBytes("old content that is longer"), Ct);
        await _writer.WriteAtomicallyAsync(Target, Encoding.UTF8.GetBytes("new"), Ct);

        (await File.ReadAllTextAsync(Target, Ct)).ShouldBe("new");
        Directory.GetFiles(Path.GetDirectoryName(Target)!).ShouldBe([Target]);
    }

    [Fact]
    public async Task LockedTarget_Throws_KeepsOriginalAndCleansUp()
    {
        // Only Windows refuses to replace an open file. On Linux and Android the rename succeeds and the
        // reader keeps the old contents, which is just as safe — there is no failure to test.
        Assert.SkipUnless(OperatingSystem.IsWindows(), "File locks block File.Replace only on Windows.");

        await _writer.WriteAtomicallyAsync(Target, Encoding.UTF8.GetBytes("original"), Ct);

        await using (new FileStream(Target, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await Should.ThrowAsync<IOException>(() => _writer.WriteAtomicallyAsync(Target, Encoding.UTF8.GetBytes("new"), Ct));
        }

        (await File.ReadAllTextAsync(Target, Ct)).ShouldBe("original");
        Directory.GetFiles(Path.GetDirectoryName(Target)!).ShouldBe([Target]);
    }
}
