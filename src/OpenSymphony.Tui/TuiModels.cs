namespace OpenSymphony.Tui;

// ht: Minimal port - core data models only, no terminal UI framework

public enum FocusPane
{
    Issues,
    Detail,
    Activity
}

public static class FocusPaneExtensions
{
    public static string Label(this FocusPane pane) => pane switch
    {
        FocusPane.Issues => "issues",
        FocusPane.Detail => "detail",
        FocusPane.Activity => "activity",
        _ => throw new ArgumentOutOfRangeException(nameof(pane))
    };
}

public enum TimelineMode
{
    Events,
    Metrics
}

public static class TimelineModeExtensions
{
    public static string Label(this TimelineMode mode) => mode switch
    {
        TimelineMode.Events => "events",
        TimelineMode.Metrics => "metrics",
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };
}

public enum ConnectionState
{
    Connecting,
    Live,
    Reconnecting
}

public static class ConnectionStateExtensions
{
    public static string Label(this ConnectionState state) => state switch
    {
        ConnectionState.Connecting => "connecting",
        ConnectionState.Live => "live",
        ConnectionState.Reconnecting => "reconnecting",
        _ => throw new ArgumentOutOfRangeException(nameof(state))
    };
}

public enum WorkspaceDiffLineKind
{
    Header,
    Hunk,
    Addition,
    Deletion,
    Context,
    Note
}

public class WorkspaceDiffLine
{
    public WorkspaceDiffLineKind Kind { get; set; }
    public string Text { get; set; } = string.Empty;

    public WorkspaceDiffLine(WorkspaceDiffLineKind kind, string text)
    {
        Kind = kind;
        Text = text;
    }
}

public enum WorkspaceFileDiffState
{
    Unloaded,
    Loading,
    Loaded,
    Unavailable
}

public class WorkspaceFileChange
{
    public string DisplayPath { get; set; } = string.Empty;
    public string QueryPath { get; set; } = string.Empty;
    public string? PreviousPath { get; set; }
    public string StatusCode { get; set; } = string.Empty;
    public ulong? Additions { get; set; }
    public ulong? Deletions { get; set; }
    public WorkspaceFileDiffState DiffState { get; set; }
    public List<WorkspaceDiffLine>? DiffLines { get; set; }
}

public class WorkspaceChangeSummary
{
    public int FilesChanged { get; set; }
    public ulong Additions { get; set; }
    public ulong Deletions { get; set; }
    public List<WorkspaceFileChange> Files { get; set; } = new();
}

public enum WorkspaceChangeState
{
    Available,
    Unavailable
}

public class WorkspaceChangeData
{
    public WorkspaceChangeState State { get; set; }
    public WorkspaceChangeSummary? Summary { get; set; }
    public string? UnavailableReason { get; set; }
}