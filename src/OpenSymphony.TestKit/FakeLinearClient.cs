using OpenSymphony.Domain;
using OpenSymphony.Linear;

namespace OpenSymphony.TestKit;

// ht: Fake Linear client for orchestrator integration tests.
//   Returns a single candidate issue, no terminal issues, and echoes state snapshots.

public sealed class FakeLinearClient : ILinearClient
{
    private readonly string _issueId;
    private readonly string _identifier;
    private readonly string _title;
    private readonly string _state;

    public FakeLinearClient(
        string issueId = "issue-123",
        string identifier = "TEST-1",
        string title = "Test fake issue",
        string state = "Todo")
    {
        _issueId = issueId;
        _identifier = identifier;
        _title = title;
        _state = state;
    }

    public Task<List<TrackerIssue>> CandidateIssues()
    {
        return Task.FromResult(new List<TrackerIssue> { BuildIssue() });
    }

    public Task<List<TrackerIssue>> TerminalIssues()
    {
        return Task.FromResult(new List<TrackerIssue>());
    }

    public Task<List<TrackerIssueStateSnapshot>> IssueStatesByIds(IEnumerable<string> issueIds)
    {
        var snapshots = new List<TrackerIssueStateSnapshot>();
        foreach (var id in issueIds)
        {
            if (id.Equals(_issueId, StringComparison.OrdinalIgnoreCase))
            {
                snapshots.Add(new TrackerIssueStateSnapshot
                {
                    Id = _issueId,
                    Identifier = _identifier,
                    State = new TrackerIssueState
                    {
                        Id = "state-todo",
                        Name = _state,
                        TrackerType = "started",
                        Kind = TrackerIssueStateKind.Started
                    },
                    UpdatedAt = DateTimeOffset.UtcNow
                });
            }
        }
        return Task.FromResult(snapshots);
    }

    private TrackerIssue BuildIssue()
    {
        return new TrackerIssue
        {
            Id = _issueId,
            Identifier = _identifier,
            Url = $"https://linear.app/issue/{_identifier}",
            Title = _title,
            Description = "Fake issue for integration testing",
            Priority = null,
            State = _state,
            StateKind = TrackerIssueStateKind.Started,
            Labels = new List<string>(),
            ParentId = null,
            Parent = null,
            ProjectMilestone = null,
            BlockedBy = new List<TrackerIssueBlocker>(),
            SubIssues = new List<TrackerIssueRef>(),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }
}
