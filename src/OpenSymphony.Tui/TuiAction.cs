using OpenSymphony.Domain;

namespace OpenSymphony.Tui;

public enum TuiActionKind
{
    SnapshotReceived,
    StreamAttached,
    ConnectionLost,
    MoveSelectionUp,
    MoveSelectionDown,
    FocusNext,
    FocusPrevious,
    ToggleDetailDiff,
    ToggleTimelineMode,
    WorkspaceStatusRequested,
    WorkspaceStatusLoaded,
    WorkspaceDiffRequested,
    WorkspaceDiffLoaded
}

public class TuiAction
{
    public TuiActionKind Kind { get; }
    public SnapshotEnvelope? Snapshot { get; init; }
    public string? Reason { get; init; }
    public string? IssueIdentifier { get; init; }
    public string? Branch { get; init; }
    public string? PrUrl { get; init; }
    public WorkspaceChangeData? Changes { get; init; }
    public string? QueryPath { get; init; }
    public List<WorkspaceDiffLine>? DiffLines { get; init; }
    public string? DiffError { get; init; }

    private TuiAction(TuiActionKind kind)
    {
        Kind = kind;
    }

    public static TuiAction SnapshotReceived(SnapshotEnvelope snapshot) =>
        new TuiAction(TuiActionKind.SnapshotReceived) { Snapshot = snapshot };

    public static TuiAction StreamAttached() =>
        new TuiAction(TuiActionKind.StreamAttached);

    public static TuiAction ConnectionLost(string reason) =>
        new TuiAction(TuiActionKind.ConnectionLost) { Reason = reason };

    public static TuiAction MoveSelectionUp() =>
        new TuiAction(TuiActionKind.MoveSelectionUp);

    public static TuiAction MoveSelectionDown() =>
        new TuiAction(TuiActionKind.MoveSelectionDown);

    public static TuiAction FocusNext() =>
        new TuiAction(TuiActionKind.FocusNext);

    public static TuiAction FocusPrevious() =>
        new TuiAction(TuiActionKind.FocusPrevious);

    public static TuiAction ToggleDetailDiff() =>
        new TuiAction(TuiActionKind.ToggleDetailDiff);

    public static TuiAction ToggleTimelineMode() =>
        new TuiAction(TuiActionKind.ToggleTimelineMode);

    public static TuiAction WorkspaceStatusRequested(string issueIdentifier) =>
        new TuiAction(TuiActionKind.WorkspaceStatusRequested) { IssueIdentifier = issueIdentifier };

    public static TuiAction WorkspaceStatusLoaded(
        string issueIdentifier,
        string branch,
        string? prUrl,
        WorkspaceChangeData changes) =>
        new TuiAction(TuiActionKind.WorkspaceStatusLoaded)
        {
            IssueIdentifier = issueIdentifier,
            Branch = branch,
            PrUrl = prUrl,
            Changes = changes
        };

    public static TuiAction WorkspaceDiffRequested(string issueIdentifier, string queryPath) =>
        new TuiAction(TuiActionKind.WorkspaceDiffRequested)
        {
            IssueIdentifier = issueIdentifier,
            QueryPath = queryPath
        };

    public static TuiAction WorkspaceDiffLoaded(
        string issueIdentifier,
        string queryPath,
        List<WorkspaceDiffLine>? diffLines,
        string? diffError) =>
        new TuiAction(TuiActionKind.WorkspaceDiffLoaded)
        {
            IssueIdentifier = issueIdentifier,
            QueryPath = queryPath,
            DiffLines = diffLines,
            DiffError = diffError
        };
}