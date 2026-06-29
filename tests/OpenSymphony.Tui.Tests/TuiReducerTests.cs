using OpenSymphony.Domain;
using OpenSymphony.Tui;
using Xunit;
using System.IO;

namespace OpenSymphony.Tui.Tests;

public class TuiReducerTests
{
    private static SnapshotEnvelope Fixture(ulong sequence, int issueCount)
    {
        var identifiers = Enumerable.Range(0, issueCount)
            .Select(i => $"COE-{255 + i}")
            .ToList();
        return FixtureWithIdentifiers(sequence, identifiers, useIdentifierInTitle: true);
    }

    private static SnapshotEnvelope FixtureWithIdentifiers(ulong sequence, List<string> identifiers, bool useIdentifierInTitle = false)
    {
        var now = new DateTimeOffset(2026, 3, 21, 20, 0, 0, TimeSpan.Zero)
            .AddSeconds((int)sequence);

        var issues = identifiers.Select((identifier, index) => new ControlPlaneIssueSnapshot(
            identifier,
            useIdentifierInTitle ? $"Issue {identifier}" : $"Issue {index}",
            "In Progress",
            ControlPlaneIssueRuntimeState.Running,
            ControlPlaneWorkerOutcome.Running,
            now,
            $"conv-{identifier}",
            $"workspace-{index}",
            (uint)index,
            null,
            null,
            null,
            0,
            0,
            0,
            false,
            new List<string>(),
            "http://127.0.0.1:3000",
            "loopback",
            "none",
            "none",
            null,
            new List<ControlPlaneConversationEvent>(),
            new List<ControlPlaneFileChange>(),
            1024 + (ulong)index * 100,
            512 + (ulong)index * 50,
            256 + (ulong)index * 25,
            0,
            false,
            false,
            false
        )).ToList();

        var snapshot = new ControlPlaneDaemonSnapshot(
            now,
            new ControlPlaneDaemonStatus(
                ControlPlaneDaemonState.Ready,
                now,
                "/tmp/opensymphony",
                "ready"),
            new ControlPlaneAgentServerStatus(
                true,
                "http://127.0.0.1:3000",
                (uint)identifiers.Count,
                "healthy"),
            new ControlPlaneMemoryServerStatus(false, false, null, "disabled"),
            new ControlPlaneMetricsSnapshot(
                1,
                0,
                512,
                512,
                256,
                1024,
                50000),
            issues,
            new List<ControlPlaneRecentEvent>
            {
                new(
                    now,
                    "COE-255",
                    ControlPlaneRecentEventKind.SnapshotPublished,
                    "snapshot updated")
            });

        return new SnapshotEnvelope(
            sequence,
            now,
            snapshot);
    }

    [Fact]
    public void AppliesSnapshotAndRendersSelectedIssue()
    {
        var state = new TuiState();
        state.Reduce(TuiAction.SnapshotReceived(Fixture(3, 2)));

        Assert.Equal(ConnectionState.Connecting, state.Connection);
        var rendered = state.RenderText(100, 20);
        Assert.Contains("conn=connecting", rendered);
        Assert.Contains("focus=issues", rendered);
        Assert.Contains("[x] ISSUES", rendered);
        Assert.Contains("[ ] ISSUE + WORKSPACE DETAIL", rendered);
        Assert.Contains("COE-255", rendered);
        Assert.Contains("Issue COE-255", rendered);
        Assert.Contains("RECENT EVENTS", rendered);
    }

    [Fact]
    public void MarksTheUiLiveAfterTheStreamAttaches()
    {
        var state = new TuiState();
        state.Reduce(TuiAction.SnapshotReceived(Fixture(3, 2)));
        state.Reduce(TuiAction.StreamAttached());

        Assert.Equal(ConnectionState.Live, state.Connection);
        var rendered = state.RenderText(100, 20);
        Assert.Contains("conn=live", rendered);
        Assert.Contains("COE-255", rendered);
    }

    [Fact]
    public void ClampsSelectionWhenNewSnapshotHasFewerIssues()
    {
        var state = new TuiState();
        state.Reduce(TuiAction.SnapshotReceived(Fixture(1, 3)));
        state.Reduce(TuiAction.MoveSelectionDown());
        state.Reduce(TuiAction.MoveSelectionDown());

        state.Reduce(TuiAction.SnapshotReceived(Fixture(2, 1)));

        Assert.Equal(0, state.SelectedIssue);
    }

    [Fact]
    public void PreservesSelectedIssueWhenSnapshotReorders()
    {
        var state = new TuiState();
        state.Reduce(TuiAction.SnapshotReceived(Fixture(1, 3)));
        state.Reduce(TuiAction.MoveSelectionDown());

        var reordered = new List<string> { "COE-257", "COE-255", "COE-256" };
        state.Reduce(TuiAction.SnapshotReceived(FixtureWithIdentifiers(2, reordered)));

        Assert.Equal(2, state.SelectedIssue);

        var rendered = state.RenderText(100, 20);
        Assert.Contains("> COE-256 [Running / In Progress]", rendered);
        Assert.Contains("branch:", rendered);
        Assert.Contains("pr:", rendered);
    }

    [Fact]
    public void CyclesFocusAndTimelineMode()
    {
        var state = new TuiState();
        state.Reduce(TuiAction.FocusNext());
        state.Reduce(TuiAction.FocusNext());
        state.Reduce(TuiAction.FocusNext());
        state.Reduce(TuiAction.ToggleTimelineMode());

        Assert.Equal(FocusPane.Issues, state.Focus);
        Assert.Equal(TimelineMode.Metrics, state.TimelineMode);

        var rendered = state.RenderText(100, 20);
        Assert.Contains("focus=issues", rendered);
        Assert.Contains("bottom=metrics", rendered);
        Assert.Contains("METRICS", rendered);
        Assert.DoesNotContain("[x] METRICS", rendered);
    }

    [Fact]
    public void KeepsTimelineVisibleWithManyIssuesInInlineLayout()
    {
        var state = new TuiState();
        state.Reduce(TuiAction.SnapshotReceived(Fixture(3, 12)));

        var rendered = state.RenderText(100, 22);

        Assert.Contains("RECENT EVENTS", rendered);
        Assert.Contains("snapshot updated", rendered);
    }

    [Fact]
    public void KeepsSelectedDetailVisibleInNarrowLayout()
    {
        var state = new TuiState();
        state.Reduce(TuiAction.SnapshotReceived(Fixture(3, 6)));

        var rendered = state.RenderText(70, 22);

        Assert.Contains("ISSUE + WORKSPACE DETAIL", rendered);
        Assert.Contains("branch: loading...", rendered);
    }

    [Fact]
    public void CyclesFocusBackwardsWithShiftTabAction()
    {
        var state = new TuiState();

        state.Reduce(TuiAction.FocusPrevious());
        Assert.Equal(FocusPane.Activity, state.Focus);

        state.Reduce(TuiAction.FocusPrevious());
        Assert.Equal(FocusPane.Detail, state.Focus);

        state.Reduce(TuiAction.FocusPrevious());
        Assert.Equal(FocusPane.Issues, state.Focus);
    }

    [Fact]
    public void KeepsSelectedIssueVisibleWhenIssueListIsWindowed()
    {
        var state = new TuiState();
        state.Reduce(TuiAction.SnapshotReceived(Fixture(3, 12)));
        for (int i = 0; i < 9; i++)
        {
            state.Reduce(TuiAction.MoveSelectionDown());
        }

        var rendered = state.RenderText(70, 22);

        Assert.Contains("> COE-264 [Running / In Progress]", rendered);
        Assert.Contains("branch: loading...", rendered);
        Assert.DoesNotContain("> COE-255 [Running / In Progress]", rendered);
    }

    [Fact]
    public void KeepsRenderingLatestSnapshotWhileReconnecting()
    {
        var state = new TuiState();
        state.Reduce(TuiAction.SnapshotReceived(Fixture(3, 2)));
        state.Reduce(TuiAction.StreamAttached());
        state.Reduce(TuiAction.ConnectionLost("stream closed"));

        var rendered = state.RenderText(100, 20);

        Assert.Contains("conn=reconnecting", rendered);
        Assert.Contains("COE-255", rendered);
        Assert.Contains("branch: loading...", rendered);
    }

    [Fact]
    public void RefreshedSnapshotsDoNotClaimLiveBeforeStreamReattaches()
    {
        var state = new TuiState();
        state.Reduce(TuiAction.SnapshotReceived(Fixture(3, 2)));
        state.Reduce(TuiAction.StreamAttached());
        state.Reduce(TuiAction.ConnectionLost("stream closed"));
        state.Reduce(TuiAction.SnapshotReceived(Fixture(4, 2)));

        Assert.Equal(ConnectionState.Reconnecting, state.Connection);
        var rendered = state.RenderText(100, 20);
        Assert.Contains("conn=reconnecting", rendered);
        Assert.Contains("seq=4", rendered);
    }

    [Fact]
    public void PreservesSelectedIssueWhenSnapshotsReorder()
    {
        var state = new TuiState();
        state.Reduce(TuiAction.SnapshotReceived(FixtureWithIdentifiers(
            1,
            new List<string> { "COE-255", "COE-256", "COE-257" })));
        state.Reduce(TuiAction.MoveSelectionDown());

        state.Reduce(TuiAction.SnapshotReceived(FixtureWithIdentifiers(
            2,
            new List<string> { "COE-257", "COE-255", "COE-256", "COE-258" })));

        Assert.Equal(2, state.SelectedIssue);
        var rendered = state.RenderText(100, 22);
        Assert.Contains("COE-256 Issue 2", rendered);
    }

    [Fact]
    public void KeepsSelectedIssueVisibleInLongIssueLists()
    {
        var state = new TuiState();
        state.Reduce(TuiAction.SnapshotReceived(Fixture(3, 12)));
        for (int i = 0; i < 8; i++)
        {
            state.Reduce(TuiAction.MoveSelectionDown());
        }

        var rendered = state.RenderText(100, 22);

        Assert.Contains("> COE-263 [Running / In Progress]", rendered);
        Assert.Contains("branch: loading...", rendered);
    }

    [Fact]
    public void RendersLoadedWorkspaceBranchPrAndFileChanges()
    {
        var state = new TuiState();
        state.Reduce(TuiAction.SnapshotReceived(Fixture(3, 1)));
        state.Reduce(TuiAction.WorkspaceStatusLoaded(
            "COE-255",
            "codex/tui-workspace-git-status",
            "https://github.com/kumanday/OpenSymphony/pull/42",
            new WorkspaceChangeData
            {
                State = WorkspaceChangeState.Available,
                Summary = new WorkspaceChangeSummary
                {
                    FilesChanged = 2,
                    Additions = 622,
                    Deletions = 280,
                    Files = new List<WorkspaceFileChange>
                    {
                        new()
                        {
                            DisplayPath = "crates/opensymphony-tui/src/lib.rs",
                            QueryPath = "crates/opensymphony-tui/src/lib.rs",
                            PreviousPath = null,
                            StatusCode = "M",
                            Additions = 594,
                            Deletions = 274,
                            DiffState = WorkspaceFileDiffState.Unloaded
                        },
                        new()
                        {
                            DisplayPath = "crates/opensymphony-tui/tests/reducer.rs",
                            QueryPath = "crates/opensymphony-tui/tests/reducer.rs",
                            PreviousPath = null,
                            StatusCode = "M",
                            Additions = 28,
                            Deletions = 6,
                            DiffState = WorkspaceFileDiffState.Unloaded
                        }
                    }
                }
            }));

        var rendered = state.RenderText(180, 24);

        Assert.Contains("branch: codex/tui-workspace-git-status", rendered);
        Assert.Contains("pr: https://github.com/kumanday/OpenSymphony/pull/42", rendered);
        Assert.Contains("2 files changed +622 -280", rendered);
        Assert.Contains("crates/opensymphony-tui/src/lib.rs", rendered);
        Assert.Contains("+594 -274", rendered);
        Assert.Contains("crates/opensymphony-tui/tests/reducer.rs", rendered);
        Assert.Contains("+28 -6", rendered);
    }

    [Fact]
    public void DetailFocusMovesChangedFileSelectionAndTogglesDiff()
    {
        var state = new TuiState();
        state.Reduce(TuiAction.SnapshotReceived(Fixture(3, 1)));
        state.Reduce(TuiAction.WorkspaceStatusLoaded(
            "COE-255",
            "codex/tui-workspace-git-status",
            null,
            new WorkspaceChangeData
            {
                State = WorkspaceChangeState.Available,
                Summary = new WorkspaceChangeSummary
                {
                    FilesChanged = 2,
                    Additions = 10,
                    Deletions = 4,
                    Files = new List<WorkspaceFileChange>
                    {
                        new()
                        {
                            DisplayPath = "src/lib.rs",
                            QueryPath = "src/lib.rs",
                            PreviousPath = null,
                            StatusCode = "M",
                            Additions = 7,
                            Deletions = 3,
                            DiffState = WorkspaceFileDiffState.Unloaded
                        },
                        new()
                        {
                            DisplayPath = "tests/reducer.rs",
                            QueryPath = "tests/reducer.rs",
                            PreviousPath = null,
                            StatusCode = "M",
                            Additions = 3,
                            Deletions = 1,
                            DiffState = WorkspaceFileDiffState.Loaded,
                            DiffLines = new List<WorkspaceDiffLine>
                            {
                                new(WorkspaceDiffLineKind.Addition, "+assert!(true);")
                            }
                        }
                    }
                }
            }));

        state.Reduce(TuiAction.FocusNext());
        state.Reduce(TuiAction.MoveSelectionDown());
        state.Reduce(TuiAction.ToggleDetailDiff());

        var rendered = state.RenderText(120, 24);

        Assert.Contains("focus=activity", rendered);
        Assert.Contains("[ ] ISSUE + WORKSPACE DETAIL", rendered);
        Assert.Contains("[x] FILE DIFF", rendered);
        Assert.Contains("MODIFIED FILES", rendered);
        Assert.DoesNotContain("[ ] MODIFIED FILES", rendered);
        Assert.Contains("▼ tests/reducer.rs", rendered);
        Assert.Contains("+3 -1", rendered);
        Assert.Contains("FILE DIFF", rendered);
        Assert.Contains("+assert!(true);", rendered);
    }
}