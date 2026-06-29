using System.Text.Json;
using OpenSymphony.Domain;

namespace OpenSymphony.Codex;

public class CodexAppServerAdapter : IHarnessAdapter
{
    private readonly CodexAppServerLaunch _launch;
    private readonly string _clientName;
    private readonly string _clientVersion;

    public CodexAppServerAdapter(
        CodexAppServerLaunch launch,
        string clientName,
        string clientVersion)
    {
        _launch = launch;
        _clientName = clientName;
        _clientVersion = clientVersion;
    }

    public static CodexAppServerAdapter LocalStdio(
        string program,
        string clientName,
        string clientVersion) =>
        new(CodexAppServerLaunch.StdioWithProgram(program), clientName, clientVersion);

    public CodexAppServerLaunch Launch => _launch;

    public CodexJsonRpcSession Session() =>
        new(_clientName, _clientVersion);

    public Result<CodexHarnessRequest> StartIssueThreadRequest(
        CodexJsonRpcSession session,
        string cwd,
        string? model,
        object config)
    {
        var request = session.ThreadStart(new CodexThreadStartParams
        {
            Cwd = cwd,
            Model = model,
            Ephemeral = false,
            Sandbox = CodexThreadSandboxMode.DangerFullAccess,
            Config = config
        });
        return Result<CodexHarnessRequest>.Ok(new CodexHarnessRequest
        {
            Lifecycle = CodexLifecycleRequest.Start,
            Request = request
        });
    }

    public Result<CodexHarnessRequest> StartIssueTurnRequest(
        CodexJsonRpcSession session,
        string threadId,
        string cwd,
        string? model,
        string workflowPrompt)
    {
        var request = session.TurnStart(new CodexTurnStartParams
        {
            ThreadId = threadId,
            Input = new List<CodexUserInput>
            {
                new CodexUserInputText { Text = workflowPrompt }
            },
            ApprovalPolicy = CodexApprovalPolicy.Never,
            Cwd = cwd,
            Model = model,
            SandboxPolicy = CodexSandboxPolicy.DangerFullAccess()
        });
        return Result<CodexHarnessRequest>.Ok(new CodexHarnessRequest
        {
            Lifecycle = CodexLifecycleRequest.Start,
            Request = request
        });
    }

    public CodexHarnessRequest ResumeIssueRequest(
        CodexJsonRpcSession session,
        string threadId,
        string cwd,
        string continuation)
    {
        var request = session.TurnStart(new CodexTurnStartParams
        {
            ThreadId = threadId,
            Input = new List<CodexUserInput>
            {
                new CodexUserInputText { Text = continuation }
            },
            ApprovalPolicy = CodexApprovalPolicy.Never,
            Cwd = cwd,
            SandboxPolicy = CodexSandboxPolicy.DangerFullAccess()
        });
        return new CodexHarnessRequest
        {
            Lifecycle = CodexLifecycleRequest.Resume,
            Request = request
        };
    }

    public CodexHarnessRequest CancelTurnRequest(
        CodexJsonRpcSession session,
        string turnId)
    {
        return new CodexHarnessRequest
        {
            Lifecycle = CodexLifecycleRequest.Cancel,
            Request = new JsonRpcRequestEnvelope
            {
                Method = "turn/cancel",
                Params = new { turnId }
            }
        };
    }

    public CodexHarnessRequest ApprovalResponse(
        CodexJsonRpcSession session,
        string approvalId,
        CodexApprovalDecision decision,
        string? message)
    {
        var @params = new Dictionary<string, object>
        {
            ["approvalId"] = approvalId,
            ["decision"] = decision.AsProtocolValue()
        };
        if (message != null)
            @params["message"] = message;

        return new CodexHarnessRequest
        {
            Lifecycle = CodexLifecycleRequest.Approval,
            Request = new JsonRpcRequestEnvelope
            {
                Method = "approval/respond",
                Params = @params
            }
        };
    }

    // IHarnessAdapter implementation
    public string HarnessKind => CodexConstants.CodexAppServerKind;
}

public enum CodexLifecycleRequest
{
    Start,
    Resume,
    Cancel,
    Approval
}

public record CodexHarnessRequest
{
    public CodexLifecycleRequest Lifecycle { get; init; }
    public JsonRpcRequestEnvelope Request { get; init; } = null!;
}

public enum CodexApprovalDecision
{
    Approve,
    Reject
}

public static class CodexApprovalDecisionExtensions
{
    public static string AsProtocolValue(this CodexApprovalDecision decision) =>
        decision switch
        {
            CodexApprovalDecision.Approve => "approve",
            CodexApprovalDecision.Reject => "reject",
            _ => throw new ArgumentOutOfRangeException(nameof(decision))
        };
}