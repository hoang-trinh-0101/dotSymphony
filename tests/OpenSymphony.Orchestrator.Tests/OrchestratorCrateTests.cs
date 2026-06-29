using OpenSymphony.Domain;
using OpenSymphony.Orchestrator;

namespace OpenSymphony.Orchestrator.Tests;

public class OrchestratorCrateTests
{
    static T Must<T, E>(Result<T, E> result) where E : notnull =>
        result.IsOk ? result.Value : throw new Exception(result.Error.ToString());

    [Fact]
    public void ExposesDomainStateMachineAsTheOrchestratorBoundary()
    {
        var issue = new NormalizedIssue(
            Must(StringIdentifier<IssueId>.New("lin_260")),
            Must(StringIdentifier<IssueIdentifier>.New("COE-260")),
            "Domain model and orchestrator state machine",
            null, 1,
            new IssueState(null, "In Progress", IssueStateCategory.Active),
            null, null, new List<string>(), null,
            new List<BlockerRef>(),
            new List<IssueRef>
            {
                new(Must(StringIdentifier<IssueId>.New("lin_261")),
                    Must(StringIdentifier<IssueIdentifier>.New("COE-261")), "Done"),
            },
            null, null);

        var workspace = new WorkspaceRecord(
            "/tmp/workspaces/COE-260",
            Must(WorkspaceKey.New("COE-260")),
            false, null, null, null);

        var run = RunAttempt.New(
            Must(StringIdentifier<WorkerId>.New("worker-1")),
            issue.Id, issue.Identifier, workspace.Path,
            TimestampMs.New(10), null, 8);

        var execution = new IssueExecution(issue, TimestampMs.New(0));
        Must(execution.AttachWorkspace(workspace));
        var claimed = Must(execution.Claim(run));
        var started = Must(claimed.StartRunning(
            TimestampMs.New(11),
            DurationMs.New(300_000),
            new ConversationMetadata(Must(StringIdentifier<ConversationId>.New("conv_260")))
            {
                ServerBaseUrl = "http://127.0.0.1:3000",
                TransportTarget = "loopback",
                HttpAuthMode = "none",
                WebsocketAuthMode = "none",
                FreshConversation = true,
                RuntimeContractVersion = "openhands-sdk-agent-server-v1",
                StreamState = RuntimeStreamState.Ready,
            }));
        var released = Must(started.Release(TimestampMs.New(12), ReleaseReason.TrackerInactive, null));

        Assert.Equal(SchedulerStatus.Released, released.Status);
        Assert.Contains("runtime state machine", OrchestratorCrate.BoundarySummary());
    }
}
