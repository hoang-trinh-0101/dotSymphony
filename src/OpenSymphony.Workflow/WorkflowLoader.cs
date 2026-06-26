using System.Collections;
using System.Globalization;
using OpenSymphony.Domain;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using YamlDotNet.Serialization.NodeDeserializers;

namespace OpenSymphony.Workflow;

public static class WorkflowLoader
{
    public static Result<WorkflowDefinition, WorkflowLoadError> LoadWorkflowFromPath(string path)
    {
        string contents;
        try
        {
            contents = File.ReadAllText(path);
        }
        catch (FileNotFoundException)
        {
            return Result<WorkflowDefinition, WorkflowLoadError>.Err(new MissingWorkflowFile(path));
        }
        catch (DirectoryNotFoundException)
        {
            return Result<WorkflowDefinition, WorkflowLoadError>.Err(new MissingWorkflowFile(path));
        }
        catch (Exception ex)
        {
            return Result<WorkflowDefinition, WorkflowLoadError>.Err(new ReadWorkflowFile(path, ex));
        }

        return ParseWorkflow(contents);
    }

    public static Result<WorkflowDefinition, WorkflowLoadError> ParseWorkflow(string source)
    {
        var split = SplitFrontMatter(source);
        if (split is null)
        {
            return Result<WorkflowDefinition, WorkflowLoadError>.Ok(
                new WorkflowDefinition(new WorkflowFrontMatter(), source));
        }

        var (frontMatterSource, promptSource) = split.Value;
        var frontMatterResult = ParseFrontMatter(frontMatterSource);
        if (frontMatterResult.IsErr)
        {
            return Result<WorkflowDefinition, WorkflowLoadError>.Err(frontMatterResult.Error);
        }

        var frontMatter = frontMatterResult.Value;
        if (frontMatter is null)
        {
            return Result<WorkflowDefinition, WorkflowLoadError>.Ok(
                new WorkflowDefinition(new WorkflowFrontMatter(), source));
        }

        return Result<WorkflowDefinition, WorkflowLoadError>.Ok(
            new WorkflowDefinition(frontMatter, promptSource));
    }

    public static (string FrontMatter, string Body)? SplitFrontMatter(string source)
    {
        // Mirror Rust split_inclusive('\n') behavior: each chunk includes its trailing newline.
        var lines = new List<(string Chunk, int Start)>();
        int start = 0;
        for (int i = 0; i < source.Length; i++)
        {
            if (source[i] == '\n')
            {
                lines.Add((source.Substring(start, i - start + 1), start));
                start = i + 1;
            }
        }
        if (start < source.Length)
        {
            lines.Add((source.Substring(start), start));
        }

        if (lines.Count == 0)
        {
            return null;
        }

        var firstLine = lines[0];
        if (TrimLine(firstLine.Chunk) != "---")
        {
            return null;
        }

        int offset = firstLine.Start + firstLine.Chunk.Length;
        for (int idx = 1; idx < lines.Count; idx++)
        {
            var line = lines[idx];
            if (TrimLine(line.Chunk) == "---")
            {
                int bodyStart = line.Start + line.Chunk.Length;
                int frontMatterStart = firstLine.Start + firstLine.Chunk.Length;
                return (source.Substring(frontMatterStart, line.Start - frontMatterStart),
                        source.Substring(bodyStart));
            }
        }

        return null;
    }

    public static Result<WorkflowFrontMatter?, WorkflowLoadError> ParseFrontMatter(string frontMatter)
    {
        object? parsed;
        try
        {
            parsed = ParseYamlValue(frontMatter);
        }
        catch (Exception ex)
        {
            return Result<WorkflowFrontMatter?, WorkflowLoadError>.Err(new WorkflowParseError(ex));
        }

        // Null / empty handling mirrors Rust serde_yaml::Value::Null branches.
        if (parsed is null || (parsed is IDictionary dict && dict.Count == 0 && string.IsNullOrWhiteSpace(frontMatter)))
        {
            if (string.IsNullOrWhiteSpace(frontMatter))
            {
                return Result<WorkflowFrontMatter?, WorkflowLoadError>.Ok(new WorkflowFrontMatter());
            }
            return Result<WorkflowFrontMatter?, WorkflowLoadError>.Ok(null);
        }

        if (parsed is not IDictionary<string, object?> map)
        {
            // Could be a list (sequence) or scalar — treat as non-map.
            return Result<WorkflowFrontMatter?, WorkflowLoadError>.Ok(null);
        }

        WorkflowFrontMatter frontMatterResult;
        try
        {
            frontMatterResult = DeserializeFrontMatter(frontMatter);
        }
        catch (Exception ex)
        {
            return Result<WorkflowFrontMatter?, WorkflowLoadError>.Err(new WorkflowParseError(ex));
        }

        if (frontMatterResult.Extensions.Count > 0)
        {
            var namespaceName = frontMatterResult.Extensions.Keys.First();
            return Result<WorkflowFrontMatter?, WorkflowLoadError>.Err(
                new UnknownTopLevelNamespace(namespaceName));
        }

        return Result<WorkflowFrontMatter?, WorkflowLoadError>.Ok(frontMatterResult);
    }

    // ht: parse raw YAML to detect map vs sequence vs scalar.
    private static object? ParseYamlValue(string yaml)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(NullNamingConvention.Instance)
            .Build();
        return deserializer.Deserialize<object?>(yaml);
    }

    // ht: deserialize front matter with custom handling for flatten extensions + options.
    internal static WorkflowFrontMatter DeserializeFrontMatter(string yaml)
    {
        var knownKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "tracker", "polling", "workspace", "hooks", "agent",
            "openhands", "routing", "codex", "logging",
        };

        var parser = new Parser(new StringReader(yaml));
        parser.Consume<StreamStart>();
        parser.Consume<DocumentStart>();

        var mapping = parser.Allow<MappingStart>();
        if (mapping is null)
        {
            return new WorkflowFrontMatter();
        }

        var tracker = new TrackerFrontMatter();
        var polling = new PollingFrontMatter();
        var workspace = new WorkspaceFrontMatter();
        var hooks = new HooksFrontMatter();
        var agent = new AgentFrontMatter();
        var openhands = new OpenHandsFrontMatter();
        var routing = new RoutingFrontMatter();
        SortedDictionary<string, object?>? codex = null;
        SortedDictionary<string, object?>? logging = null;
        var extensions = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        var subDeserializer = CreateSubDeserializer();

        while (!parser.Accept<MappingEnd>(out _))
        {
            var keyScalar = parser.Consume<Scalar>();
            var key = keyScalar.Value;
            var valueNode = ReadRawNode(parser);

            switch (key)
            {
                case "tracker":
                    tracker = subDeserializer.Deserialize<TrackerFrontMatter>(NodeToYaml(valueNode));
                    break;
                case "polling":
                    polling = subDeserializer.Deserialize<PollingFrontMatter>(NodeToYaml(valueNode));
                    break;
                case "workspace":
                    workspace = subDeserializer.Deserialize<WorkspaceFrontMatter>(NodeToYaml(valueNode));
                    break;
                case "hooks":
                    hooks = subDeserializer.Deserialize<HooksFrontMatter>(NodeToYaml(valueNode));
                    break;
                case "agent":
                    agent = subDeserializer.Deserialize<AgentFrontMatter>(NodeToYaml(valueNode));
                    break;
                case "openhands":
                    openhands = DeserializeOpenHandsFrontMatter(NodeToYaml(valueNode), subDeserializer);
                    break;
                case "routing":
                    routing = subDeserializer.Deserialize<RoutingFrontMatter>(NodeToYaml(valueNode));
                    break;
                case "codex":
                    codex = subDeserializer.Deserialize<SortedDictionary<string, object?>>(NodeToYaml(valueNode));
                    break;
                case "logging":
                    logging = subDeserializer.Deserialize<SortedDictionary<string, object?>>(NodeToYaml(valueNode));
                    break;
                default:
                    extensions[key] = valueNode;
                    break;
            }
        }

        parser.Consume<MappingEnd>();
        parser.Consume<DocumentEnd>();
        parser.Consume<StreamEnd>();

        return new WorkflowFrontMatter
        {
            Tracker = tracker,
            Polling = polling,
            Workspace = workspace,
            Hooks = hooks,
            Agent = agent,
            OpenHands = openhands,
            Routing = routing,
            Codex = codex,
            Logging = logging,
            Extensions = extensions,
        };
    }

    // ht: OpenHands front matter needs custom handling for confirmation_policy options and agent options flatten.
    private static OpenHandsFrontMatter DeserializeOpenHandsFrontMatter(string yaml, IDeserializer sub)
    {
        var knownKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "transport", "local_server", "conversation", "websocket", "mcp",
        };

        var parser = new Parser(new StringReader(yaml));
        parser.Consume<StreamStart>();
        parser.Consume<DocumentStart>();
        var mapping = parser.Allow<MappingStart>();
        if (mapping is null) return new OpenHandsFrontMatter();

        var result = new OpenHandsFrontMatter();
        while (!parser.Accept<MappingEnd>(out _))
        {
            var keyScalar = parser.Consume<Scalar>();
            var key = keyScalar.Value;
            var valueNode = ReadRawNode(parser);

            switch (key)
            {
                case "transport":
                    result = result with { Transport = sub.Deserialize<OpenHandsTransportFrontMatter>(NodeToYaml(valueNode)) };
                    break;
                case "local_server":
                    result = result with { LocalServer = sub.Deserialize<OpenHandsLocalServerFrontMatter>(NodeToYaml(valueNode)) };
                    break;
                case "conversation":
                    result = result with { Conversation = DeserializeConversation(NodeToYaml(valueNode), sub) };
                    break;
                case "websocket":
                    result = result with { Websocket = sub.Deserialize<OpenHandsWebSocketFrontMatter>(NodeToYaml(valueNode)) };
                    break;
                case "mcp":
                    result = result with { LegacyLinearBridge = valueNode };
                    break;
            }
        }
        parser.Consume<MappingEnd>();
        parser.Consume<DocumentEnd>();
        parser.Consume<StreamEnd>();
        return result;
    }

    private static OpenHandsConversationFrontMatter DeserializeConversation(string yaml, IDeserializer sub)
    {
        var knownKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "reuse_policy", "persistence_dir_relative", "max_iterations",
            "stuck_detection", "confirmation_policy", "agent",
        };

        var parser = new Parser(new StringReader(yaml));
        parser.Consume<StreamStart>();
        parser.Consume<DocumentStart>();
        var mapping = parser.Allow<MappingStart>();
        if (mapping is null) return new OpenHandsConversationFrontMatter();

        var result = new OpenHandsConversationFrontMatter();
        while (!parser.Accept<MappingEnd>(out _))
        {
            var keyScalar = parser.Consume<Scalar>();
            var key = keyScalar.Value;
            var valueNode = ReadRawNode(parser);

            switch (key)
            {
                case "reuse_policy":
                    result = result with { ReusePolicy = (string?)valueNode };
                    break;
                case "persistence_dir_relative":
                    result = result with { PersistenceDirRelative = (string?)valueNode };
                    break;
                case "max_iterations":
                    result = result with { MaxIterations = ToIntegerLike(valueNode) };
                    break;
                case "stuck_detection":
                    result = result with { StuckDetection = (bool?)valueNode };
                    break;
                case "confirmation_policy":
                    result = result with { ConfirmationPolicy = DeserializeConfirmationPolicy(NodeToYaml(valueNode), sub) };
                    break;
                case "agent":
                    result = result with { Agent = DeserializeAgent(NodeToYaml(valueNode), sub) };
                    break;
            }
        }
        parser.Consume<MappingEnd>();
        parser.Consume<DocumentEnd>();
        parser.Consume<StreamEnd>();
        return result;
    }

    private static OpenHandsConfirmationPolicyFrontMatter DeserializeConfirmationPolicy(string yaml, IDeserializer sub)
    {
        var parser = new Parser(new StringReader(yaml));
        parser.Consume<StreamStart>();
        parser.Consume<DocumentStart>();
        var mapping = parser.Allow<MappingStart>();
        if (mapping is null) return new OpenHandsConfirmationPolicyFrontMatter();

        var result = new OpenHandsConfirmationPolicyFrontMatter();
        var options = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        while (!parser.Accept<MappingEnd>(out _))
        {
            var keyScalar = parser.Consume<Scalar>();
            var key = keyScalar.Value;
            var valueNode = ReadRawNode(parser);
            if (key == "kind")
            {
                result = result with { Kind = (string?)valueNode };
            }
            else
            {
                options[key] = valueNode;
            }
        }
        parser.Consume<MappingEnd>();
        parser.Consume<DocumentEnd>();
        parser.Consume<StreamEnd>();
        return result with { Options = options };
    }

    private static OpenHandsConversationAgentFrontMatter DeserializeAgent(string yaml, IDeserializer sub)
    {
        var knownKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "kind", "llm", "condenser", "tools", "include_default_tools", "log_completions",
        };

        var parser = new Parser(new StringReader(yaml));
        parser.Consume<StreamStart>();
        parser.Consume<DocumentStart>();
        var mapping = parser.Allow<MappingStart>();
        if (mapping is null) return new OpenHandsConversationAgentFrontMatter();

        var result = new OpenHandsConversationAgentFrontMatter();
        var options = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        while (!parser.Accept<MappingEnd>(out _))
        {
            var keyScalar = parser.Consume<Scalar>();
            var key = keyScalar.Value;
            var valueNode = ReadRawNode(parser);

            switch (key)
            {
                case "kind":
                    result = result with { Kind = (string?)valueNode };
                    break;
                case "llm":
                    result = result with { Llm = DeserializeLlm(NodeToYaml(valueNode), sub) };
                    break;
                case "condenser":
                    result = result with { Condenser = sub.Deserialize<OpenHandsConversationCondenserFrontMatter>(NodeToYaml(valueNode)) };
                    break;
                case "tools":
                    result = result with { Tools = sub.Deserialize<List<OpenHandsConversationToolFrontMatter>>(NodeToYaml(valueNode)) };
                    break;
                case "include_default_tools":
                    result = result with { IncludeDefaultTools = sub.Deserialize<List<string>>(NodeToYaml(valueNode)) };
                    break;
                case "log_completions":
                    result = result with { LogCompletions = (bool?)valueNode };
                    break;
                default:
                    options[key] = valueNode;
                    break;
            }
        }
        parser.Consume<MappingEnd>();
        parser.Consume<DocumentEnd>();
        parser.Consume<StreamEnd>();
        return result with { Options = options };
    }

    private static OpenHandsLlmFrontMatter DeserializeLlm(string yaml, IDeserializer sub)
    {
        var knownKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "model", "api_key_env", "base_url_env", "credential_mode", "subscription",
        };

        var parser = new Parser(new StringReader(yaml));
        parser.Consume<StreamStart>();
        parser.Consume<DocumentStart>();
        var mapping = parser.Allow<MappingStart>();
        if (mapping is null) return new OpenHandsLlmFrontMatter();

        var result = new OpenHandsLlmFrontMatter();
        var options = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        while (!parser.Accept<MappingEnd>(out _))
        {
            var keyScalar = parser.Consume<Scalar>();
            var key = keyScalar.Value;
            var valueNode = ReadRawNode(parser);

            switch (key)
            {
                case "model":
                    result = result with { Model = (string?)valueNode };
                    break;
                case "api_key_env":
                    result = result with { ApiKeyEnv = (string?)valueNode };
                    break;
                case "base_url_env":
                    result = result with { BaseUrlEnv = (string?)valueNode };
                    break;
                case "credential_mode":
                    result = result with { CredentialMode = (string?)valueNode };
                    break;
                case "subscription":
                    result = result with { Subscription = sub.Deserialize<OpenHandsSubscriptionCredentialFrontMatter>(NodeToYaml(valueNode)) };
                    break;
                default:
                    options[key] = valueNode;
                    break;
            }
        }
        parser.Consume<MappingEnd>();
        parser.Consume<DocumentEnd>();
        parser.Consume<StreamEnd>();
        return result with { Options = options };
    }

    // ht: ReadRawNode reads a full YAML node (mapping/sequence/scalar) and returns a raw object tree.
    // Re-serializes to string for sub-deserialization via NodeToYaml.
    private static object? ReadRawNode(IParser parser)
    {
        if (parser.Accept<Scalar>(out var scalar))
        {
            parser.MoveNext();
            if (scalar.Style == ScalarStyle.Plain)
            {
                if (long.TryParse(scalar.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
                    return l;
                if (double.TryParse(scalar.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                    return d;
                if (bool.TryParse(scalar.Value, out var b))
                    return b;
                if (scalar.Value == "null" || scalar.Value == "~" || scalar.Value == "")
                    return null;
            }
            return scalar.Value;
        }

        if (parser.Accept<MappingStart>(out _))
        {
            parser.Consume<MappingStart>();
            var dict = new SortedDictionary<string, object?>(StringComparer.Ordinal);
            while (!parser.Accept<MappingEnd>(out _))
            {
                var keyScalar = parser.Consume<Scalar>();
                var val = ReadRawNode(parser);
                dict[keyScalar.Value] = val;
            }
            parser.Consume<MappingEnd>();
            return dict;
        }

        if (parser.Accept<SequenceStart>(out _))
        {
            parser.Consume<SequenceStart>();
            var list = new List<object?>();
            while (!parser.Accept<SequenceEnd>(out _))
            {
                list.Add(ReadRawNode(parser));
            }
            parser.Consume<SequenceEnd>();
            return list;
        }

        return null;
    }

    // ht: NodeToYaml re-serializes a raw object tree back to YAML for sub-deserialization.
    private static string NodeToYaml(object? node)
    {
        var serializer = new SerializerBuilder()
            .WithNamingConvention(NullNamingConvention.Instance)
            .Build();
        return serializer.Serialize(node);
    }

    private static IntegerLike? ToIntegerLike(object? node)
    {
        if (node is null) return null;
        if (node is long l) return IntegerLike.FromInteger(l);
        if (node is int i) return IntegerLike.FromInteger(i);
        if (node is string s) return IntegerLike.FromString(s);
        return IntegerLike.FromString(node.ToString());
    }

    private static IDeserializer CreateSubDeserializer() => new DeserializerBuilder()
        .WithNamingConvention(NullNamingConvention.Instance)
        .WithNodeDeserializer(new IntegerLikeNodeDeserializer(), s => s.InsteadOf<ObjectNodeDeserializer>())
        .Build();

    private static string TrimLine(string line) => line.TrimEnd('\r', '\n');
}
