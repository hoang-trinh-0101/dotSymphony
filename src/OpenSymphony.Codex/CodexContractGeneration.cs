namespace OpenSymphony.Codex;

public enum CodexContractArtifact
{
    JsonSchema,
    TypeScript
}

public class CodexContractGeneration
{
    private readonly string _program;
    private readonly CodexContractArtifact _artifact;
    private readonly string _outDir;

    public CodexContractGeneration(string program, CodexContractArtifact artifact, string outDir)
    {
        _program = program;
        _artifact = artifact;
        _outDir = outDir;
    }

    public static CodexContractGeneration JsonSchema(string outDir) =>
        JsonSchemaWithProgram("codex", outDir);

    public static CodexContractGeneration JsonSchemaWithProgram(string program, string outDir) =>
        new(program, CodexContractArtifact.JsonSchema, outDir);

    public static CodexContractGeneration TypeScript(string outDir) =>
        TypeScriptWithProgram("codex", outDir);

    public static CodexContractGeneration TypeScriptWithProgram(string program, string outDir) =>
        new(program, CodexContractArtifact.TypeScript, outDir);

    public CodexContractArtifact Artifact => _artifact;

    public (string program, List<string> args) ToCommand()
    {
        var generator = _artifact switch
        {
            CodexContractArtifact.JsonSchema => "generate-json-schema",
            CodexContractArtifact.TypeScript => "generate-ts",
            _ => throw new ArgumentOutOfRangeException()
        };
        return (_program, new List<string>
        {
            "app-server",
            generator,
            "--out",
            _outDir
        });
    }
}