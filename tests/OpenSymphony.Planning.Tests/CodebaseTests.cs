using System.IO;
using System.Text.Json;
using OpenSymphony.Planning;

namespace OpenSymphony.Planning.Tests;

public class CodebaseTests
{
    private static string CreateTestRepo(string root)
    {
        File.WriteAllText(root + "/Cargo.toml", """
[workspace]
members = ["."]
resolver = "2"

[workspace.dependencies]
serde = { version = "1", features = ["derive"] }
tokio = { version = "1", features = ["full"] }

[dependencies]
serde = { workspace = true }
tokio = { workspace = true }
""");

        foreach (var crateName in new[] { "opensymphony-core", "opensymphony-linear", "opensymphony-testkit" })
        {
            var crateDir = Path.Combine(root, "crates", crateName);
            Directory.CreateDirectory(Path.Combine(crateDir, "src"));
            File.WriteAllText(Path.Combine(crateDir, "Cargo.toml"),
                $"[package]\nname = \"{crateName}\"\nversion = \"0.1.0\"\nedition = \"2024\"\n\n[dependencies]\nserde = {{ workspace = true }}");
            File.WriteAllText(Path.Combine(crateDir, "src", "lib.rs"), "// stub\n");
        }

        Directory.CreateDirectory(Path.Combine(root, "src"));
        File.WriteAllText(Path.Combine(root, "src", "main.rs"), "fn main() {}\n");
        File.WriteAllText(root + "/package.json", "{ \"name\": \"opensymphony\", \"version\": \"1.0.0\" }");
        File.WriteAllText(root + "/README.md", "# OpenSymphony\n");
        File.WriteAllText(root + "/.gitignore", "/target\n/node_modules\n");
        File.WriteAllText(root + "/rust-toolchain.toml", "[toolchain]\nchannel = \"1.93\"\n");
        File.WriteAllText(root + "/clippy.toml", "");
        File.WriteAllText(root + "/rustfmt.toml", "max_width = 100\n");

        var clientDir = Path.Combine(root, "crates", "opensymphony-linear", "src");
        File.WriteAllText(Path.Combine(clientDir, "client.rs"), "// HTTP client stub\n");

        return root;
    }

    [Fact]
    public void AnalyzeDetectsRustPackagesAndLanguages()
    {
        using var tmp = new TempDir();
        var root = CreateTestRepo(tmp.Path);
        var analyzer = new CodebaseAnalyzer(root);
        var analysis = analyzer.Analyze();
        Assert.True(analysis.IsOk);
        var a = analysis.Value;
        Assert.True(a.TotalRustFiles > 0);
        Assert.True(a.TotalFiles > 0);
        Assert.Contains(a.Languages, l => l.Language == "rust");
        Assert.Contains(a.Packages, p => p.Name == "opensymphony-core");
        Assert.Contains(a.Packages, p => p.Name == "opensymphony-linear");
    }

    [Fact]
    public void AnalyzeDetectsBuildSystems()
    {
        using var tmp = new TempDir();
        var root = CreateTestRepo(tmp.Path);
        var analyzer = new CodebaseAnalyzer(root);
        var analysis = analyzer.Analyze();
        Assert.True(analysis.IsOk);
        var a = analysis.Value;
        Assert.Contains("cargo", a.BuildSystems);
        Assert.Contains("npm", a.BuildSystems);
    }

    [Fact]
    public void AnalyzeDetectsOwnershipSignals()
    {
        using var tmp = new TempDir();
        var root = CreateTestRepo(tmp.Path);
        var analyzer = new CodebaseAnalyzer(root);
        var analysis = analyzer.Analyze();
        Assert.True(analysis.IsOk);
        var a = analysis.Value;
        Assert.Contains(a.OwnershipFiles, s => s.FilePath == "Cargo.toml");
        Assert.Contains(a.OwnershipFiles, s => s.FilePath == "README.md");
        Assert.Contains(a.OwnershipFiles, s => s.FilePath == ".gitignore");
    }

    [Fact]
    public void AnalyzeDetectsConventions()
    {
        using var tmp = new TempDir();
        var root = CreateTestRepo(tmp.Path);
        var analyzer = new CodebaseAnalyzer(root);
        var analysis = analyzer.Analyze();
        Assert.True(analysis.IsOk);
        var a = analysis.Value;
        Assert.Contains(a.Conventions, c => c.Area == "build");
        Assert.Contains(a.Conventions, c => c.Area == "linting");
        Assert.Contains(a.Conventions, c => c.Area == "formatting");
    }

    [Fact]
    public void AnalyzeDetectsIntegrationPoints()
    {
        using var tmp = new TempDir();
        var root = CreateTestRepo(tmp.Path);
        var analyzer = new CodebaseAnalyzer(root);
        var analysis = analyzer.Analyze();
        Assert.True(analysis.IsOk);
        Assert.NotEmpty(analysis.Value.IntegrationPoints);
    }

    [Fact]
    public void AnalyzeDetectsMixedLanguageRisk()
    {
        using var tmp = new TempDir();
        var root = CreateTestRepo(tmp.Path);
        var pkgDir = Path.Combine(root, "packages", "ui-core");
        Directory.CreateDirectory(pkgDir);
        File.WriteAllText(Path.Combine(pkgDir, "package.json"), "{ \"name\": \"ui-core\" }");

        var analyzer = new CodebaseAnalyzer(root);
        var analysis = analyzer.Analyze();
        Assert.True(analysis.IsOk);
        Assert.Contains(analysis.Value.Risks, r => r.Category == RiskCategory.Maintenance);
    }

    [Fact]
    public void AnalyzeSerializesToJson()
    {
        using var tmp = new TempDir();
        var root = CreateTestRepo(tmp.Path);
        var analyzer = new CodebaseAnalyzer(root);
        var analysis = analyzer.Analyze();
        Assert.True(analysis.IsOk);

        var json = JsonSerializer.Serialize(analysis.Value);
        Assert.Contains("opensymphony-core", json);
        Assert.Contains("cargo", json);

        var deserialized = JsonSerializer.Deserialize<CodebaseAnalysis>(json)!;
        Assert.Equal(analysis.Value.RootPath, deserialized.RootPath);
        Assert.Equal(analysis.Value.TotalFiles, deserialized.TotalFiles);
    }

    [Fact]
    public void AnalyzeReturnsErrorForNonexistentDirectory()
    {
        var analyzer = new CodebaseAnalyzer("/nonexistent/path/that/does/not/exist");
        var result = analyzer.Analyze();
        Assert.True(result.IsErr);
        Assert.Equal(CodebaseAnalysisErrorKind.NotADirectory, result.Error.Kind);
        Assert.Contains("nonexistent", result.Error.Path);
    }
}

file sealed class TempDir : IDisposable
{
    public string Path { get; }
    public TempDir() { Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName()); Directory.CreateDirectory(Path); }
    public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
}
