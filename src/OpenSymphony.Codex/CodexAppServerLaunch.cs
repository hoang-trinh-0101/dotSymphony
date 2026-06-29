namespace OpenSymphony.Codex;

public abstract record CodexAppServerTransport
{
    public record Stdio : CodexAppServerTransport;
    public record WebSocketLoopback(ushort Port) : CodexAppServerTransport;
}

public record CodexWebSocketAuth
{
    public string? TokenFile { get; init; }
    public string? TokenSha256 { get; init; }
    public string? SharedSecretFile { get; init; }
    public string? Issuer { get; init; }
    public string? Audience { get; init; }
    public ulong? MaxClockSkewSeconds { get; init; }

    public static CodexWebSocketAuth CapabilityToken(string tokenFile, string tokenSha256) =>
        new() { TokenFile = tokenFile, TokenSha256 = tokenSha256 };

    public static CodexWebSocketAuth SignedBearerToken(
        string sharedSecretFile,
        string issuer,
        string audience,
        ulong? maxClockSkewSeconds = null) =>
        new()
        {
            SharedSecretFile = sharedSecretFile,
            Issuer = issuer,
            Audience = audience,
            MaxClockSkewSeconds = maxClockSkewSeconds
        };
}

public class CodexAppServerLaunch
{
    private readonly string _program;
    public CodexAppServerTransport Transport { get; }
    public List<string> ExtraArgs { get; }
    public CodexWebSocketAuth? WebSocketAuth { get; }

    public CodexAppServerLaunch(
        string program,
        CodexAppServerTransport transport,
        List<string>? extraArgs = null,
        CodexWebSocketAuth? webSocketAuth = null)
    {
        _program = program;
        Transport = transport;
        ExtraArgs = extraArgs ?? new();
        WebSocketAuth = webSocketAuth;
    }

    public static CodexAppServerLaunch Stdio() => StdioWithProgram("codex");

    public static CodexAppServerLaunch StdioWithProgram(string program) =>
        new(program, new CodexAppServerTransport.Stdio());

    public static CodexAppServerLaunch LoopbackWebSocket(ushort port) =>
        LoopbackWebSocketWithProgram("codex", port);

    public static CodexAppServerLaunch LoopbackWebSocketWithProgram(string program, ushort port) =>
        new(program, new CodexAppServerTransport.WebSocketLoopback(port));

    public string Program => _program;

    public (string program, List<string> args) ToCommand() => (_program, CommandArgs());

    public List<string> CommandArgs()
    {
        var args = new List<string>
        {
            "--dangerously-bypass-hook-trust",
            "app-server"
        };
        args.AddRange(ExtraArgs);

        switch (Transport)
        {
            case CodexAppServerTransport.Stdio:
                args.Add("--stdio");
                break;
            case CodexAppServerTransport.WebSocketLoopback ws:
                args.Add("--listen");
                args.Add($"ws://127.0.0.1:{ws.Port}");
                if (WebSocketAuth != null)
                {
                    if (WebSocketAuth.TokenFile != null)
                    {
                        args.AddRange(new[]
                        {
                            "--ws-auth", "capability-token",
                            "--ws-token-file", WebSocketAuth.TokenFile,
                            "--ws-token-sha256", WebSocketAuth.TokenSha256 ?? string.Empty
                        });
                    }
                    else if (WebSocketAuth.SharedSecretFile != null)
                    {
                        args.AddRange(new[]
                        {
                            "--ws-auth", "signed-bearer-token",
                            "--ws-shared-secret-file", WebSocketAuth.SharedSecretFile,
                            "--ws-issuer", WebSocketAuth.Issuer ?? string.Empty,
                            "--ws-audience", WebSocketAuth.Audience ?? string.Empty
                        });
                        if (WebSocketAuth.MaxClockSkewSeconds.HasValue)
                        {
                            args.AddRange(new[]
                            {
                                "--ws-max-clock-skew-seconds",
                                WebSocketAuth.MaxClockSkewSeconds.Value.ToString()
                            });
                        }
                    }
                }
                break;
        }

        return args;
    }
}