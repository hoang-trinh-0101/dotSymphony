namespace OpenSymphony.Memory.Tests;

/// <summary>
/// ht: minimal temp directory helper for tests. Creates a unique directory and cleans up on dispose.
/// </summary>
public sealed class TempDir : IDisposable
{
    public string Path { get; }

    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "os-mem-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path);
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, true); } catch { }
    }
}
