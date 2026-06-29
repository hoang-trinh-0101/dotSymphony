using OpenSymphony.Domain;

namespace OpenSymphony.Tui;

public partial class TuiState
{
    public FocusPane Focus { get; private set; }
    public TimelineMode TimelineMode { get; private set; }
    public ConnectionState Connection { get; private set; }
    public int SelectedIssue { get; private set; }
    public SnapshotEnvelope? LatestSnapshot { get; private set; }
    public string StatusLine { get; private set; }
    private Dictionary<string, WorkspaceStatusEntry> WorkspaceStatus { get; set; }
    private int SelectedChangedFile { get; set; }
    private bool DetailDiffOpen { get; set; }
    private string? DetailIssueIdentifier { get; set; }
    private int ConversationScrollOffset { get; set; }
    private int DiffScrollOffset { get; set; }

    public TuiState()
    {
        Focus = FocusPane.Issues;
        TimelineMode = TimelineMode.Events;
        Connection = ConnectionState.Connecting;
        SelectedIssue = 0;
        LatestSnapshot = null;
        StatusLine = "connecting to control plane";
        WorkspaceStatus = new Dictionary<string, WorkspaceStatusEntry>();
        SelectedChangedFile = 0;
        DetailDiffOpen = false;
        DetailIssueIdentifier = null;
        ConversationScrollOffset = 0;
        DiffScrollOffset = 0;
    }

    public void Reduce(TuiAction action)
    {
        switch (action.Kind)
        {
            case TuiActionKind.SnapshotReceived:
                if (action.Snapshot != null)
                {
                    var selectedIssueIdentifier = GetSelectedIssue()?.Identifier;
                    LatestSnapshot = action.Snapshot;
                    if (Connection != ConnectionState.Live)
                    {
                        StatusLine = Connection switch
                        {
                            ConnectionState.Connecting => "bootstrap snapshot loaded; waiting for live stream",
                            ConnectionState.Reconnecting => "snapshot refreshed; waiting for live stream",
                            _ => "live control-plane stream"
                        };
                    }
                    RestoreSelection(selectedIssueIdentifier);
                    RetainWorkspaceStatusForVisibleIssues();
                    SyncDetailState();
                }
                break;

            case TuiActionKind.StreamAttached:
                Connection = ConnectionState.Live;
                StatusLine = "live control-plane stream";
                break;

            case TuiActionKind.ConnectionLost:
                Connection = ConnectionState.Reconnecting;
                StatusLine = $"reconnecting after: {action.Reason}";
                break;

            case TuiActionKind.MoveSelectionUp:
                switch (Focus)
                {
                    case FocusPane.Issues:
                        MoveIssueSelectionUp();
                        break;
                    case FocusPane.Detail:
                        MoveChangedFileSelectionUp();
                        break;
                    case FocusPane.Activity:
                        if (DetailDiffOpen)
                            MoveDiffScrollUp();
                        else
                            MoveConversationScrollUp();
                        break;
                }
                break;

            case TuiActionKind.MoveSelectionDown:
                switch (Focus)
                {
                    case FocusPane.Issues:
                        MoveIssueSelectionDown();
                        break;
                    case FocusPane.Detail:
                        MoveChangedFileSelectionDown();
                        break;
                    case FocusPane.Activity:
                        if (DetailDiffOpen)
                            MoveDiffScrollDown();
                        else
                            MoveConversationScrollDown();
                        break;
                }
                break;

            case TuiActionKind.FocusNext:
                Focus = Focus switch
                {
                    FocusPane.Issues => FocusPane.Detail,
                    FocusPane.Detail => FocusPane.Activity,
                    FocusPane.Activity => FocusPane.Issues,
                    _ => Focus
                };
                break;

            case TuiActionKind.FocusPrevious:
                Focus = Focus switch
                {
                    FocusPane.Issues => FocusPane.Activity,
                    FocusPane.Detail => FocusPane.Issues,
                    FocusPane.Activity => FocusPane.Detail,
                    _ => Focus
                };
                break;

            case TuiActionKind.ToggleDetailDiff:
                if ((Focus == FocusPane.Detail || Focus == FocusPane.Activity) && SelectedFileChange() != null)
                {
                    DetailDiffOpen = !DetailDiffOpen;
                    if (DetailDiffOpen)
                    {
                        Focus = FocusPane.Activity;
                        DiffScrollOffset = 0;
                    }
                    else
                    {
                        DiffScrollOffset = 0;
                    }
                }
                SyncDetailState();
                break;

            case TuiActionKind.ToggleTimelineMode:
                TimelineMode = TimelineMode == TimelineMode.Events ? TimelineMode.Metrics : TimelineMode.Events;
                break;

            case TuiActionKind.WorkspaceStatusRequested:
                if (action.IssueIdentifier != null)
                {
                    WorkspaceStatus[action.IssueIdentifier] = new WorkspaceStatusEntry { IsLoading = true };
                }
                break;

            case TuiActionKind.WorkspaceStatusLoaded:
                if (action.IssueIdentifier != null && action.Branch != null && action.Changes != null)
                {
                    var mergedChanges = MergeWorkspaceChanges(action.IssueIdentifier, action.Changes);
                    WorkspaceStatus[action.IssueIdentifier] = new WorkspaceStatusEntry
                    {
                        IsLoading = false,
                        Branch = action.Branch,
                        PrUrl = action.PrUrl,
                        Changes = mergedChanges
                    };
                    SyncDetailState();
                }
                break;

            case TuiActionKind.WorkspaceDiffRequested:
                if (action.IssueIdentifier != null && action.QueryPath != null)
                {
                    SetFileDiffState(action.IssueIdentifier, action.QueryPath, new WorkspaceFileDiffStateData { State = WorkspaceFileDiffState.Loading });
                }
                break;

            case TuiActionKind.WorkspaceDiffLoaded:
                if (action.IssueIdentifier != null && action.QueryPath != null)
                {
                    var diffState = action.DiffError != null
                        ? new WorkspaceFileDiffStateData { State = WorkspaceFileDiffState.Unavailable, Error = action.DiffError }
                        : new WorkspaceFileDiffStateData { State = WorkspaceFileDiffState.Loaded, Lines = action.DiffLines };
                    SetFileDiffState(action.IssueIdentifier, action.QueryPath, diffState);
                    SyncDetailState();
                }
                break;
        }
    }

    private void MoveIssueSelectionUp()
    {
        if (LatestSnapshot?.Snapshot.Issues.Count > 0 && SelectedIssue > 0)
        {
            SelectedIssue--;
        }
    }

    private void MoveIssueSelectionDown()
    {
        if (LatestSnapshot != null && SelectedIssue < LatestSnapshot.Snapshot.Issues.Count - 1)
        {
            SelectedIssue++;
        }
    }

    private void MoveChangedFileSelectionUp()
    {
        var fileCount = SelectedFileChangeCount();
        if (fileCount > 0 && SelectedChangedFile > 0)
        {
            SelectedChangedFile--;
        }
    }

    private void MoveChangedFileSelectionDown()
    {
        var fileCount = SelectedFileChangeCount();
        if (fileCount > 0 && SelectedChangedFile < fileCount - 1)
        {
            SelectedChangedFile++;
        }
    }

    private void MoveDiffScrollUp()
    {
        if (DiffScrollOffset > 0)
        {
            DiffScrollOffset--;
        }
    }

    private void MoveDiffScrollDown()
    {
        DiffScrollOffset++;
    }

    private void MoveConversationScrollUp()
    {
        if (ConversationScrollOffset > 0)
        {
            ConversationScrollOffset--;
        }
    }

    private void MoveConversationScrollDown()
    {
        ConversationScrollOffset++;
    }

    private ControlPlaneIssueSnapshot? GetSelectedIssue()
    {
        if (LatestSnapshot == null || LatestSnapshot.Snapshot.Issues.Count == 0)
            return null;
        if (SelectedIssue >= LatestSnapshot.Snapshot.Issues.Count)
            return null;
        return LatestSnapshot.Snapshot.Issues[SelectedIssue];
    }

    private void RestoreSelection(string? identifier)
    {
        if (identifier == null || LatestSnapshot == null)
            return;

        for (int i = 0; i < LatestSnapshot.Snapshot.Issues.Count; i++)
        {
            if (LatestSnapshot.Snapshot.Issues[i].Identifier == identifier)
            {
                SelectedIssue = i;
                return;
            }
        }
        SelectedIssue = Math.Min(SelectedIssue, Math.Max(0, LatestSnapshot.Snapshot.Issues.Count - 1));
    }

    private void RetainWorkspaceStatusForVisibleIssues()
    {
        if (LatestSnapshot == null)
            return;

        var visibleIds = new HashSet<string>(LatestSnapshot.Snapshot.Issues.Select(i => i.Identifier));
        var keysToRemove = WorkspaceStatus.Keys.Where(k => !visibleIds.Contains(k)).ToList();
        foreach (var key in keysToRemove)
        {
            WorkspaceStatus.Remove(key);
        }
    }

    private void SyncDetailState()
    {
        var issue = GetSelectedIssue();
        if (issue != null)
        {
            DetailIssueIdentifier = issue.Identifier;
        }
    }

    private WorkspaceChangeData MergeWorkspaceChanges(string issueIdentifier, WorkspaceChangeData newChanges)
    {
        // ht: Simple merge - new changes replace old
        return newChanges;
    }

    private void SetFileDiffState(string issueIdentifier, string queryPath, WorkspaceFileDiffStateData state)
    {
        if (!WorkspaceStatus.TryGetValue(issueIdentifier, out var entry) || entry.IsLoading)
            return;

        if (entry.Changes?.Summary?.Files != null)
        {
            var file = entry.Changes.Summary.Files.FirstOrDefault(f => f.QueryPath == queryPath);
            if (file != null)
            {
                file.DiffState = state.State;
                file.DiffLines = state.Lines;
            }
        }
    }

    private WorkspaceFileChange? SelectedFileChange()
    {
        var issue = GetSelectedIssue();
        if (issue == null)
            return null;

        if (!WorkspaceStatus.TryGetValue(issue.Identifier, out var entry) || entry.IsLoading)
            return null;

        if (entry.Changes?.Summary?.Files == null || entry.Changes.Summary.Files.Count == 0)
            return null;

        if (SelectedChangedFile >= entry.Changes.Summary.Files.Count)
            return null;

        return entry.Changes.Summary.Files[SelectedChangedFile];
    }

    private int SelectedFileChangeCount()
    {
        var issue = GetSelectedIssue();
        if (issue == null)
            return 0;

        if (!WorkspaceStatus.TryGetValue(issue.Identifier, out var entry) || entry.IsLoading)
            return 0;

        return entry.Changes?.Summary?.Files.Count ?? 0;
    }

    private class WorkspaceStatusEntry
    {
        public bool IsLoading { get; set; }
        public string? Branch { get; set; }
        public string? PrUrl { get; set; }
        public WorkspaceChangeData? Changes { get; set; }
    }

    private class WorkspaceFileDiffStateData
    {
        public WorkspaceFileDiffState State { get; set; }
        public List<WorkspaceDiffLine>? Lines { get; set; }
        public string? Error { get; set; }
    }
}