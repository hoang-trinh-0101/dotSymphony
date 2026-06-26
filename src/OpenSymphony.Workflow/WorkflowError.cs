namespace OpenSymphony.Workflow;

// ht: abstract record base + sealed subtypes mirrors the Rust enum's #[error] messages.
public abstract record WorkflowLoadError
{
    public virtual string Message => ToString();
}

public sealed record MissingWorkflowFile(string Path) : WorkflowLoadError
{
    public override string Message => $"workflow file not found: {Path}";
}

public sealed record ReadWorkflowFile(string Path, Exception Source) : WorkflowLoadError
{
    public override string Message => $"failed to read workflow file {Path}: {Source}";
}

public sealed record MissingFrontMatterTerminator : WorkflowLoadError
{
    public override string Message => "workflow front matter is missing a closing `---` delimiter";
}

public sealed record WorkflowParseError(Exception Source) : WorkflowLoadError
{
    public override string Message => $"failed to parse workflow front matter: {Source}";
}

public sealed record WorkflowFrontMatterNotAMap : WorkflowLoadError
{
    public override string Message => "workflow front matter must decode to a YAML map";
}

public sealed record UnknownTopLevelNamespace(string Namespace) : WorkflowLoadError
{
    public override string Message => $"unknown top-level workflow namespace `{Namespace}`";
}

public abstract record WorkflowConfigError
{
    public virtual string Message => ToString();
}

public sealed record MissingRequiredField(string Field) : WorkflowConfigError
{
    public override string Message => $"missing required config field `{Field}`";
}

public sealed record MissingEnvironmentVariable(string Field, string Variable) : WorkflowConfigError
{
    public override string Message => $"missing required environment variable `{Variable}` for `{Field}`";
}

public sealed record UnsupportedTrackerKind(string Kind) : WorkflowConfigError
{
    public override string Message => $"unsupported tracker kind `{Kind}`";
}

public sealed record InvalidInteger(string Field, string Value) : WorkflowConfigError
{
    public override string Message => $"invalid integer for `{Field}`: `{Value}`";
}

public sealed record InvalidField(string Field, string MessageText) : WorkflowConfigError
{
    public override string Message => $"invalid config for `{Field}`: {MessageText}";
}

public sealed record RemovedField(string Field, string MessageText) : WorkflowConfigError
{
    public override string Message => $"removed config field `{Field}`: {MessageText}";
}

public abstract record PromptTemplateError
{
    public virtual string Message => ToString();
}

public sealed record PromptTemplateContext(Exception Source) : PromptTemplateError
{
    public override string Message => $"failed to serialize template context: {Source}";
}

public sealed record PromptTemplateParse(Exception Source) : PromptTemplateError
{
    public override string Message => $"failed to parse prompt template: {Source}";
}

public sealed record PromptTemplateRender(Exception Source) : PromptTemplateError
{
    public override string Message => $"failed to render prompt template: {Source}";
}
