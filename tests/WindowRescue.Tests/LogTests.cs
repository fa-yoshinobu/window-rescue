using System.IO;
using System.Text;
using WindowRescue.Core;

namespace WindowRescue.Tests;

public sealed class LogTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "WindowRescue.LogTests", Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_directory, "test.log");

    [Fact]
    public void RotationRetainsOnlyCurrentAndPreviousGeneration()
    {
        var log = new RotatingLog(FilePath, 128);
        log.Write("first-" + new string('a', 80));
        log.Write("second-" + new string('b', 80));
        log.Write("third-" + new string('c', 80));
        Assert.Contains("third-", File.ReadAllText(FilePath));
        Assert.Contains("second-", File.ReadAllText(FilePath + ".1"));
        Assert.Equal(2, Directory.GetFiles(_directory).Length);
        Assert.All(Directory.GetFiles(_directory), f => Assert.InRange(new FileInfo(f).Length, 1, 128));
    }

    [Fact]
    public void OversizedUnicodeEntryStaysWithinLimitAndRemainsValidUtf8()
    {
        var log = new RotatingLog(FilePath, 128);
        log.Write(string.Concat(Enumerable.Repeat("窓😀", 200)));
        var bytes = File.ReadAllBytes(FilePath);
        Assert.InRange(bytes.Length, 1, 128);
        Assert.EndsWith("\n", new UTF8Encoding(false, true).GetString(bytes));
    }

    [Fact]
    public void UnwritableLocationDoesNotThrow()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, "block directory creation");
        var log = new RotatingLog(Path.Combine(FilePath, "blocked.log"));
        Assert.Null(Record.Exception(() => log.Error("failure", new IOException("test"))));
    }

    [Fact]
    public void ErrorIncludesCauseButNotPotentiallyPrivateExceptionMessage()
    {
        var log = new RotatingLog(FilePath);
        log.Error("operation", new InvalidOperationException("PRIVATE WINDOW TITLE", new IOException("PRIVATE INNER TITLE")));
        string text = File.ReadAllText(FilePath);
        Assert.Contains(nameof(InvalidOperationException), text);
        Assert.Contains(nameof(IOException), text);
        Assert.DoesNotContain("PRIVATE", text);
    }

    [Fact]
    public void ConcurrentWritersKeepWholeEntries()
    {
        var log = new RotatingLog(FilePath);
        Parallel.For(0, 100, i => log.Write($"entry-{i}"));
        var lines = File.ReadAllLines(FilePath);
        Assert.Equal(100, lines.Length);
        Assert.Equal(100, lines.Select(l => l[(l.IndexOf(' ') + 1)..]).Distinct().Count());
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
