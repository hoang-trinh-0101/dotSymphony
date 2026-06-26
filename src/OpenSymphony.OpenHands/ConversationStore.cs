using System.Security.Cryptography;
using System.Text;

namespace OpenSymphony.OpenHands;

// ht: minimal port of opensymphony-openhands conversation_store.rs.

public static class ConversationStoreConstants
{
    public const string OPENHANDS_CONVERSATIONS_PATH_ENV = "OH_CONVERSATIONS_PATH";
}

public enum ConversationStoreKind
{
    Active,
    Archived,
    Legacy,
}

public static class ConversationStoreKindExtensions
{
    public static string AsStr(this ConversationStoreKind kind) => kind switch
    {
        ConversationStoreKind.Active => "active",
        ConversationStoreKind.Archived => "archived",
        ConversationStoreKind.Legacy => "legacy",
        _ => kind.ToString(),
    };
}

public sealed record LocatedConversation(ConversationStoreKind Kind, string Path);

public sealed record LocatedWorkspaceConversation(
    string ConversationId, string WorkingDir, ConversationStoreKind Kind, string Path);

public sealed record ConversationStoreScanReport(
    List<LocatedWorkspaceConversation> Conversations,
    List<string> Warnings);

public abstract record ConversationMoveOutcome
{
    public sealed record Moved(ConversationStoreKind From, string FromPath, ConversationStoreKind To, string ToPath) : ConversationMoveOutcome;
    public sealed record AlreadyInTarget(ConversationStoreKind Kind, string Path) : ConversationMoveOutcome;
    public sealed record Missing : ConversationMoveOutcome;
}

public sealed class ConversationStoreError : Exception
{
    public ConversationStoreError(string message) : base(message) { }
}

public sealed record OpenHandsConversationStorePaths
{
    public string RepoKey { get; init; } = "";
    public string LegacyRoot { get; init; } = "";
    public string RepoRoot { get; init; } = "";
    public string Active { get; init; } = "";
    public string Archived { get; init; } = "";

    public static OpenHandsConversationStorePaths ForToolDir(string toolDir, string targetRepo)
    {
        var canonicalRepo = Path.GetFullPath(targetRepo);
        var repoKey = RepoStoreKey(canonicalRepo);
        var legacyRoot = Path.Combine(toolDir, "workspace", "conversations");
        var repoRoot = Path.Combine(legacyRoot, "repos", repoKey);
        return new()
        {
            RepoKey = repoKey,
            LegacyRoot = legacyRoot,
            RepoRoot = repoRoot,
            Active = Path.Combine(repoRoot, "active"),
            Archived = Path.Combine(repoRoot, "archived"),
        };
    }

    public void EnsureActiveAndArchived()
    {
        Directory.CreateDirectory(Active);
        Directory.CreateDirectory(Archived);
    }

    public string PathFor(ConversationStoreKind kind) => kind switch
    {
        ConversationStoreKind.Active => Active,
        ConversationStoreKind.Archived => Archived,
        ConversationStoreKind.Legacy => LegacyRoot,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public LocatedConversation? LocateConversation(string conversationId)
    {
        var names = ConversationDirNames(conversationId);
        foreach (var kind in new[] { ConversationStoreKind.Active, ConversationStoreKind.Archived, ConversationStoreKind.Legacy })
        {
            var storePath = PathFor(kind);
            foreach (var name in names)
            {
                var path = Path.Combine(storePath, name);
                if (Directory.Exists(path))
                    return new LocatedConversation(kind, path);
            }
        }
        return null;
    }

    private static string RepoStoreKey(string repoPath)
    {
        // ht: SHA256 of the canonical path, first 16 hex chars.
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(repoPath));
        return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
    }

    private static string[] ConversationDirNames(string conversationId)
    {
        // ht: OpenHands stores conversations as UUID directories; accept both raw and with prefix.
        var trimmed = conversationId.Trim();
        return [trimmed, $"conv-{trimmed}"];
    }
}
