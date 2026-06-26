using System.Collections;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenSymphony.Domain;

namespace OpenSymphony.Planning;

public enum PackageKind { Library, Binary, TestUtilities, Frontend }

public enum OwnershipSignalType { CargoWorkspace, Readme, License, Gitignore, Codeowners, PackageJson }

public enum IntegrationType { CrossCrateDependency, ApiClient, DatabaseAccess, ExternalService, SharedSchema }

public enum RiskCategory { Complexity, Security, Coupling, Testing, Performance, Maintenance }

public enum RiskSeverity { Low, Medium, High }

public sealed record LanguageSignature(string Language, int FileCount, List<string> SamplePaths);

public sealed record PackageInfo(string Name, string RelativePath, PackageKind Kind, List<string> Dependencies);

public sealed record OwnershipSignal(string FilePath, OwnershipSignalType SignalType, string ContentHint);

public sealed record IntegrationPoint(string SourcePackage, string? TargetPackage, IntegrationType IntegrationType, string Detail);

public sealed record Convention(string Area, string Description, string EvidencePath);

public sealed record AnalysisRisk(RiskCategory Category, RiskSeverity Severity, string Description, string AffectedPath);

public sealed record CodebaseAnalysis(
    string RootPath,
    List<LanguageSignature> Languages,
    List<PackageInfo> Packages,
    List<string> BuildSystems,
    List<OwnershipSignal> OwnershipFiles,
    List<IntegrationPoint> IntegrationPoints,
    List<Convention> Conventions,
    List<AnalysisRisk> Risks,
    int TotalFiles,
    int TotalRustFiles,
    int TotalTypescriptFiles);

public enum CodebaseAnalysisErrorKind { NotADirectory, Io, Toml, Json }

public sealed record CodebaseAnalysisError(CodebaseAnalysisErrorKind Kind, string Path, string Message)
{
    public override string ToString() => Kind switch
    {
        CodebaseAnalysisErrorKind.NotADirectory => $"not a directory: {Path}",
        CodebaseAnalysisErrorKind.Io => $"IO error reading {Path}: {Message}",
        CodebaseAnalysisErrorKind.Toml => $"TOML parse error in {Path}: {Message}",
        CodebaseAnalysisErrorKind.Json => $"JSON parse error in {Path}: {Message}",
        _ => Message,
    };
}

public sealed class CodebaseAnalyzer
{
    private readonly string _root;

    public CodebaseAnalyzer(string root) => _root = root;

    public Result<CodebaseAnalysis, CodebaseAnalysisError> Analyze()
    {
        if (!Directory.Exists(_root))
            return Result<CodebaseAnalysis, CodebaseAnalysisError>.Err(
                new CodebaseAnalysisError(CodebaseAnalysisErrorKind.NotADirectory, _root, ""));

        var walker = new RepoWalker(_root);
        var fileInventory = walker.Walk();
        if (fileInventory.IsErr)
            return Result<CodebaseAnalysis, CodebaseAnalysisError>.Err(fileInventory.Error);

        var inventory = fileInventory.Value;
        var languages = CodebaseHelpers.DetectLanguages(inventory);
        var packages = CodebaseHelpers.DetectPackages(_root, inventory);
        if (packages.IsErr)
            return Result<CodebaseAnalysis, CodebaseAnalysisError>.Err(packages.Error);
        var buildSystems = CodebaseHelpers.DetectBuildSystems(_root);
        var ownershipFiles = CodebaseHelpers.DetectOwnershipSignals(_root, inventory);
        var integrationPoints = CodebaseHelpers.DetectIntegrationPoints(packages.Value, inventory);
        var conventions = CodebaseHelpers.DetectConventions(_root, inventory);
        var risks = CodebaseHelpers.AssessRisks(_root, packages.Value, integrationPoints);

        var totalRust = inventory.Keys.Count(p => Path.GetExtension(p) == ".rs");
        var totalTs = inventory.Keys.Count(p =>
        {
            var ext = Path.GetExtension(p);
            return ext == ".ts" || ext == ".tsx";
        });

        return Result<CodebaseAnalysis, CodebaseAnalysisError>.Ok(new CodebaseAnalysis(
            _root, languages, packages.Value, buildSystems, ownershipFiles,
            integrationPoints, conventions, risks, inventory.Count, totalRust, totalTs));
    }

    private static string? GitCommitSha(string root)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                Arguments = "rev-parse HEAD",
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc is null) return null;
            var output = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit();
            if (proc.ExitCode != 0) return null;
            return string.IsNullOrEmpty(output) ? null : output;
        }
        catch { return null; }
    }
}

file sealed class RepoWalker
{
    private readonly string _root;
    private readonly HashSet<string> _excludeDirs;

    public RepoWalker(string root)
    {
        _root = root;
        _excludeDirs = new HashSet<string> { "node_modules", ".git", "target", ".venv", "__pycache__", "dist", "build" };
    }

    public Result<SortedDictionary<string, long>, CodebaseAnalysisError> Walk()
    {
        var inventory = new SortedDictionary<string, long>();
        var err = WalkDir(_root, inventory);
        return err is not null
            ? Result<SortedDictionary<string, long>, CodebaseAnalysisError>.Err(err)
            : Result<SortedDictionary<string, long>, CodebaseAnalysisError>.Ok(inventory);
    }

    private CodebaseAnalysisError? WalkDir(string dir, SortedDictionary<string, long> inventory)
    {
        IEnumerable<string> entries;
        try { entries = Directory.EnumerateFileSystemEntries(dir); }
        catch (UnauthorizedAccessException) when (dir != _root) { return null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new CodebaseAnalysisError(CodebaseAnalysisErrorKind.Io, dir, ex.Message);
        }

        foreach (var fullPath in entries)
        {
            var fileName = Path.GetFileName(fullPath);
            try
            {
                var attrs = File.GetAttributes(fullPath);
                if ((attrs & FileAttributes.Directory) != 0 && (attrs & FileAttributes.ReparsePoint) == 0)
                {
                    if (_excludeDirs.Contains(fileName)) continue;
                    if (fileName.StartsWith('.') && fileName != ".github") continue;
                    WalkDir(fullPath, inventory);
                }
                else
                {
                    var relative = Path.GetRelativePath(_root, fullPath).Replace('\\', '/');
                    long size = 0;
                    try { size = new FileInfo(fullPath).Length; } catch { }
                    inventory[relative] = size;
                }
            }
            catch (UnauthorizedAccessException) { continue; }
        }
        return null;
    }
}

file static class CodebaseHelpers
{
    public static string DerivePackageName(string relativePath, List<PackageInfo> packages)
    {
        var parts = relativePath.Split('/');
        var cratesIdx = Array.IndexOf(parts, "crates");
        if (cratesIdx >= 0 && cratesIdx + 1 < parts.Length)
            return parts[cratesIdx + 1];

        var pathStr = relativePath.Replace('\\', '/');
        var matching = packages
            .Where(p => pathStr.StartsWith(p.RelativePath, StringComparison.Ordinal))
            .OrderByDescending(p => p.RelativePath.Length)
            .Select(p => p.Name)
            .FirstOrDefault();
        return matching ?? relativePath;
    }

    public static List<LanguageSignature> DetectLanguages(SortedDictionary<string, long> inventory)
    {
        var langMap = new SortedDictionary<string, (int Count, List<string> Samples)>();

        foreach (var path in inventory.Keys)
        {
            var ext = Path.GetExtension(path);
            string? language = ext switch
            {
                ".rs" => "rust",
                ".ts" or ".tsx" => "typescript",
                ".js" or ".jsx" => "javascript",
                ".toml" => "toml",
                ".json" => "json",
                ".yaml" or ".yml" => "yaml",
                ".md" => "markdown",
                ".graphql" => "graphql",
                ".sh" => "shell",
                ".py" => "python",
                _ => null,
            };
            if (language is null) continue;

            if (!langMap.TryGetValue(language, out var entry))
            {
                entry = (0, new List<string>());
                langMap[language] = entry;
            }
            entry.Count++;
            if (entry.Samples.Count < 3)
                entry.Samples.Add(path);
        }

        return langMap.Select(kv => new LanguageSignature(kv.Key, kv.Value.Count, kv.Value.Samples)).ToList();
    }

    public static Result<List<PackageInfo>, CodebaseAnalysisError> DetectPackages(string root, SortedDictionary<string, long> _)
    {
        var packages = new List<PackageInfo>();

        // Detect Rust crates
        var cratesDir = Path.Combine(root, "crates");
        if (Directory.Exists(cratesDir))
        {
            foreach (var dir in Directory.EnumerateDirectories(cratesDir))
            {
                var cargoToml = Path.Combine(dir, "Cargo.toml");
                if (!File.Exists(cargoToml)) continue;

                var name = Path.GetFileName(dir);
                var (deps, hasBin) = ParseCargoToml(cargoToml);
                var kind = (hasBin || File.Exists(Path.Combine(dir, "src", "main.rs")) || Directory.Exists(Path.Combine(dir, "src", "bin")))
                    ? PackageKind.Binary
                    : (name.Contains("test") || name.Contains("testkit"))
                        ? PackageKind.TestUtilities
                        : PackageKind.Library;

                packages.Add(new PackageInfo(name, $"crates/{name}", kind, deps));
            }
        }

        // Detect TypeScript packages
        foreach (var pkgDir in new[] { "packages", "apps" })
        {
            var dir = Path.Combine(root, pkgDir);
            if (!Directory.Exists(dir)) continue;
            foreach (var subDir in Directory.EnumerateDirectories(dir))
            {
                var pkgJson = Path.Combine(subDir, "package.json");
                if (!File.Exists(pkgJson)) continue;

                var name = Path.GetFileName(subDir);
                var deps = ExtractNpmDeps(pkgJson);
                var kind = pkgDir == "apps" ? PackageKind.Binary : PackageKind.Frontend;
                packages.Add(new PackageInfo(name, $"{pkgDir}/{name}", kind, deps));
            }
        }

        return Result<List<PackageInfo>, CodebaseAnalysisError>.Ok(packages);
    }

    public static (List<string> Deps, bool HasBin) ParseCargoToml(string path)
    {
        var deps = new List<string>();
        var hasBin = false;
        try
        {
            var lines = File.ReadAllLines(path);
            var inDeps = false;
            var inDevDeps = false;
            var inBuildDeps = false;
            var inBin = false;
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith('['))
                {
                    inDeps = trimmed == "[dependencies]";
                    inDevDeps = trimmed == "[dev-dependencies]";
                    inBuildDeps = trimmed == "[build-dependencies]";
                    inBin = trimmed == "[[bin]]";
                    continue;
                }
                if (inBin) { hasBin = true; continue; }
                if (inDeps || inDevDeps || inBuildDeps)
                {
                    var eqIdx = trimmed.IndexOf('=');
                    if (eqIdx > 0)
                    {
                        var key = trimmed[..eqIdx].Trim();
                        if (!deps.Contains(key)) deps.Add(key);
                    }
                }
            }
        }
        catch { }
        deps.Sort(StringComparer.Ordinal);
        return (deps, hasBin);
    }

    public static List<string> ExtractNpmDeps(string path)
    {
        var deps = new SortedDictionary<string, object?>();
        try
        {
            var content = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.TryGetProperty("dependencies", out var d) && d.ValueKind == JsonValueKind.Object)
                foreach (var p in d.EnumerateObject()) deps[p.Name] = null;
            if (doc.RootElement.TryGetProperty("devDependencies", out var dd) && dd.ValueKind == JsonValueKind.Object)
                foreach (var p in dd.EnumerateObject()) deps.TryAdd(p.Name, null);
            if (doc.RootElement.TryGetProperty("peerDependencies", out var pd) && pd.ValueKind == JsonValueKind.Object)
                foreach (var p in pd.EnumerateObject()) deps.TryAdd(p.Name, null);
            if (doc.RootElement.TryGetProperty("optionalDependencies", out var od) && od.ValueKind == JsonValueKind.Object)
                foreach (var p in od.EnumerateObject()) deps.TryAdd(p.Name, null);
        }
        catch { }
        return deps.Keys.ToList();
    }

    public static List<string> DetectBuildSystems(string root)
    {
        var systems = new List<string>();
        if (File.Exists(Path.Combine(root, "Cargo.toml"))) systems.Add("cargo");
        if (File.Exists(Path.Combine(root, "package.json"))) systems.Add("npm");
        if (File.Exists(Path.Combine(root, "Makefile"))) systems.Add("make");
        if (File.Exists(Path.Combine(root, "justfile"))) systems.Add("just");
        return systems;
    }

    public static List<OwnershipSignal> DetectOwnershipSignals(string root, SortedDictionary<string, long> inventory)
    {
        var signals = new List<OwnershipSignal>();
        var signalFiles = new (string File, OwnershipSignalType Type, string Hint)[]
        {
            ("Cargo.toml", OwnershipSignalType.CargoWorkspace, "Rust workspace definition"),
            ("README.md", OwnershipSignalType.Readme, "Project documentation"),
            ("LICENSE", OwnershipSignalType.License, "License file"),
            (".gitignore", OwnershipSignalType.Gitignore, "Git ignore rules"),
            (".github/CODEOWNERS", OwnershipSignalType.Codeowners, "Code ownership mapping"),
            ("package.json", OwnershipSignalType.PackageJson, "Node.js package definition"),
        };

        foreach (var (file, type, hint) in signalFiles)
        {
            var normalized = file.Replace('\\', '/');
            if (inventory.ContainsKey(normalized) || File.Exists(Path.Combine(root, file)))
                signals.Add(new OwnershipSignal(file, type, hint));
        }
        return signals;
    }

    public static List<IntegrationPoint> DetectIntegrationPoints(List<PackageInfo> packages, SortedDictionary<string, long> inventory)
    {
        var points = new List<IntegrationPoint>();

        // Cross-crate dependencies from Cargo.toml
        foreach (var pkg in packages)
        {
            foreach (var dep in pkg.Dependencies)
            {
                if (packages.Any(p => p.Name == dep))
                {
                    points.Add(new IntegrationPoint(pkg.Name, $"crates/{dep}", IntegrationType.CrossCrateDependency, $"Cargo dependency: {dep}"));
                }
            }
        }

        // Detect API/client patterns
        foreach (var path in inventory.Keys)
        {
            var ext = Path.GetExtension(path);
            if (ext != ".rs") continue;
            if ((path.Contains("client") || path.Contains("transport")) && Path.GetDirectoryName(path) is not null)
            {
                var pkgName = DerivePackageName(path, packages);
                points.Add(new IntegrationPoint(pkgName, null, IntegrationType.ApiClient, $"HTTP/API client: {path}"));
            }
            if (path.Contains("duckdb") || path.Contains("database") || path.Contains("db_"))
            {
                var pkgName = DerivePackageName(path, packages);
                points.Add(new IntegrationPoint(pkgName, null, IntegrationType.DatabaseAccess, $"Database access: {path}"));
            }
        }

        return points;
    }

    public static List<Convention> DetectConventions(string root, SortedDictionary<string, long> inventory)
    {
        var conventions = new List<Convention>();

        if (File.Exists(Path.Combine(root, "Cargo.toml")))
            conventions.Add(new Convention("build", "Rust workspace with shared dependency versions via [workspace.dependencies]", "Cargo.toml"));
        if (File.Exists(Path.Combine(root, "clippy.toml")))
            conventions.Add(new Convention("linting", "Custom Clippy lint configuration", "clippy.toml"));
        if (File.Exists(Path.Combine(root, "rustfmt.toml")))
            conventions.Add(new Convention("formatting", "Custom rustfmt configuration", "rustfmt.toml"));

        var seenTestCrates = new HashSet<string>();
        foreach (var path in inventory.Keys)
        {
            var parts = path.Split('/');
            var isTestFile = parts.Length >= 2 && parts[^2] == "tests";
            if (path.StartsWith("crates/") && parts.Length >= 3 && isTestFile)
            {
                var crateName = parts[1];
                if (seenTestCrates.Add(crateName))
                    conventions.Add(new Convention("testing", $"Integration tests in crates/{crateName}/tests/", path));
            }
        }

        if (File.Exists(Path.Combine(root, "tsconfig.json")))
            conventions.Add(new Convention("typescript", "TypeScript with project references", "tsconfig.json"));

        return conventions;
    }

    public static List<AnalysisRisk> AssessRisks(string root, List<PackageInfo> packages, List<IntegrationPoint> integrationPoints)
    {
        var risks = new List<AnalysisRisk>();

        // Check for high coupling
        var couplingCounts = new SortedDictionary<string, int>();
        foreach (var ip in integrationPoints)
        {
            if (ip.TargetPackage is { } target)
            {
                couplingCounts.TryGetValue(target, out var c);
                couplingCounts[target] = c + 1;
            }
        }
        foreach (var (pkg, count) in couplingCounts)
        {
            if (count >= 5)
                risks.Add(new AnalysisRisk(RiskCategory.Coupling, RiskSeverity.High, $"High coupling: {pkg} is depended on by {count} packages", pkg));
        }

        // Check for missing test utilities
        var hasTestkit = packages.Any(p => p.Name.Contains("test") || p.Name.Contains("testkit"));
        if (!hasTestkit && packages.Count > 3)
            risks.Add(new AnalysisRisk(RiskCategory.Testing, RiskSeverity.Medium, "No dedicated test utility crate found; shared test infrastructure may be duplicated", "crates/"));

        // Mixed language/build system risk
        var hasRustPackages = packages.Any(p => p.RelativePath.StartsWith("crates/"));
        var hasTsPackages = packages.Any(p => p.RelativePath.StartsWith("packages/") || p.RelativePath.StartsWith("apps/"));
        var hasRootCargo = File.Exists(Path.Combine(root, "Cargo.toml"));
        var hasRootNpm = File.Exists(Path.Combine(root, "package.json"));
        var mixedSubPackages = hasRustPackages && hasTsPackages;
        var mixedRoot = hasRootCargo && hasRootNpm;
        if (mixedSubPackages || mixedRoot)
            risks.Add(new AnalysisRisk(RiskCategory.Maintenance, RiskSeverity.Medium, "Mixed Rust/TypeScript monorepo requires coordinated build and CI strategies", "root"));

        return risks;
    }
}
