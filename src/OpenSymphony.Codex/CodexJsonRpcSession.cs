using System.Text.Json;

namespace OpenSymphony.Codex;

public class CodexJsonRpcSession
{
    private ulong _nextId = 1;
    public string ClientName { get; }
    public string ClientVersion { get; }

    public CodexJsonRpcSession(string clientName, string clientVersion)
    {
        ClientName = clientName;
        ClientVersion = clientVersion;
    }

    public JsonRpcRequestEnvelope Initialize()
    {
        return Request("initialize", new
        {
            clientInfo = new
            {
                name = ClientName,
                version = ClientVersion
            },
            capabilities = new { }
        });
    }

    public JsonRpcRequestEnvelope ThreadStart(CodexThreadStartParams @params)
    {
        var json = JsonSerializer.Serialize(@params);
        var value = JsonSerializer.Deserialize<object>(json)!;
        return Request("thread/start", value);
    }

    public JsonRpcRequestEnvelope TurnStart(CodexTurnStartParams @params)
    {
        var json = JsonSerializer.Serialize(@params);
        var value = JsonSerializer.Deserialize<object>(json)!;
        return Request("turn/start", value);
    }

    private JsonRpcRequestEnvelope Request(string method, object @params)
    {
        var id = _nextId++;
        return new JsonRpcRequestEnvelope
        {
            Id = id,
            Method = method,
            Params = @params
        };
    }

    public static string EncodeLine(JsonRpcRequestEnvelope request) =>
        JsonSerializer.Serialize(request) + "\n";
}