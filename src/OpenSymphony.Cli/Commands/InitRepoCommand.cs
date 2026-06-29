using System.CommandLine;
using System.Text;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace OpenSymphony.Cli;

/// <summary>
/// Initialize the current target repository with OpenSymphony files.
/// </summary>
public static class InitRepoCommand
{
    private const string DefaultTemplateBaseUrl = "https://raw.githubusercontent.com/kumanday/OpenSymphony-template/refs/heads/main/";
    private const string DefaultLlmModel = "openai/accounts/fireworks/models/glm-5p1";
    private const string DefaultLlmBaseUrl = "https://api.fireworks.ai/inference/v1";

    public static Command Create()
    {
        var command = new Command("init", "Initialize the current target repository with OpenSymphony files");

        var nonInteractiveOption = new Option<bool>("--non-interactive", "Run without interactive prompts");
        var aiPrReviewOption = new Option<bool>("--ai-pr-review", "Scaffold automated OpenHands AI PR review without prompting");
        var linearProjectSlugOption = new Option<string?>("--linear-project-slug", "Linear project slug/key to write into WORKFLOW.md");
        var conflictPolicyOption = new Option<InitConflictPolicy?>("--conflict-policy", "Apply this policy for existing generated files");
        var commitAndPushOption = new Option<bool>("--commit-and-push", "Commit and push generated bootstrap files when git preflight allows it");
        var configureGithubOption = new Option<bool>("--configure-github", "Configure GitHub Actions variables, secret, and review label with gh");
        var llmModelOption = new Option<string?>("--llm-model", "Model shown in the LLM_MODEL export snippet");
        var llmBaseUrlOption = new Option<string?>("--llm-base-url", "Base URL shown in the LLM_BASE_URL export snippet");

        command.Add(nonInteractiveOption);
        command.Add(aiPrReviewOption);
        command.Add(linearProjectSlugOption);
        command.Add(conflictPolicyOption);
        command.Add(commitAndPushOption);
        command.Add(configureGithubOption);
        command.Add(llmModelOption);
        command.Add(llmBaseUrlOption);

        command.SetHandler(async (context) =>
        {
            var nonInteractive = context.ParseResult.GetValueForOption(nonInteractiveOption);
            var aiPrReview = context.ParseResult.GetValueForOption(aiPrReviewOption);
            var linearProjectSlug = context.ParseResult.GetValueForOption(linearProjectSlugOption);
            var conflictPolicy = context.ParseResult.GetValueForOption(conflictPolicyOption);
            var commitAndPush = context.ParseResult.GetValueForOption(commitAndPushOption);
            var configureGithub = context.ParseResult.GetValueForOption(configureGithubOption);
            var llmModel = context.ParseResult.GetValueForOption(llmModelOption);
            var llmBaseUrl = context.ParseResult.GetValueForOption(llmBaseUrlOption);
            var cancellationToken = context.GetCancellationToken();

            var args = new InitArgs(
                nonInteractive,
                aiPrReview,
                linearProjectSlug,
                conflictPolicy,
                commitAndPush,
                configureGithub,
                llmModel,
                llmBaseUrl
            );

            var exitCode = await RunAsync(args, cancellationToken);
            context.ExitCode = exitCode;
        });

        return command;
    }

    private static async Task<int> RunAsync(InitArgs args, CancellationToken cancellationToken)
    {
        try
        {
            args.Validate();

            var currentDir = Directory.GetCurrentDirectory();
            Console.WriteLine($"Initializing OpenSymphony in {currentDir}");

            var opensymphonyDir = Path.Combine(currentDir, ".opensymphony");
            var configPath = Path.Combine(opensymphonyDir, "config.yaml");
            var workflowPath = Path.Combine(currentDir, "WORKFLOW.md");

            var conflictPolicy = args.ConflictPolicy ?? InitConflictPolicy.Prompt;

            // Create .opensymphony directory
            if (!Directory.Exists(opensymphonyDir))
            {
                Directory.CreateDirectory(opensymphonyDir);
                Console.WriteLine($"Created {opensymphonyDir}");
            }

            // Create config.yaml
            var configAction = PlanFileAction(configPath, conflictPolicy, args.NonInteractive);
            if (configAction == FileAction.Abort)
            {
                Console.WriteLine($"Initialization aborted by --conflict-policy abort for existing {configPath}");
                return 1;
            }

            if (configAction == FileAction.Create || configAction == FileAction.Overwrite)
            {
                var config = GenerateDefaultConfig(args.LlmModel, args.LlmBaseUrl);
                await File.WriteAllTextAsync(configPath, config, cancellationToken);
                Console.WriteLine($"{(configAction == FileAction.Create ? "Created" : "Overwrote")} {configPath}");
            }
            else if (configAction == FileAction.Skip)
            {
                Console.WriteLine($"Skipped {configPath} (already exists)");
            }

            // Create WORKFLOW.md
            var workflowAction = PlanFileAction(workflowPath, conflictPolicy, args.NonInteractive);
            if (workflowAction == FileAction.Abort)
            {
                Console.WriteLine($"Initialization aborted by --conflict-policy abort for existing {workflowPath}");
                return 1;
            }

            if (workflowAction == FileAction.Create || workflowAction == FileAction.Overwrite)
            {
                var workflow = GenerateWorkflowTemplate(args.LinearProjectSlug);
                await File.WriteAllTextAsync(workflowPath, workflow, cancellationToken);
                Console.WriteLine($"{(workflowAction == FileAction.Create ? "Created" : "Overwrote")} {workflowPath}");
            }
            else if (workflowAction == FileAction.Skip)
            {
                Console.WriteLine($"Skipped {workflowPath} (already exists)");
            }

            // TODO: Handle AI PR review scaffolding if args.AiPrReview
            // TODO: Handle GitHub configuration if args.ConfigureGithub
            // TODO: Handle git commit and push if args.CommitAndPush
            // TODO: Call memory initialization

            Console.WriteLine("OpenSymphony initialization complete.");
            return 0;
        }
        catch (InitCommandException ex)
        {
            Console.Error.WriteLine($"opensymphony init failed: {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"opensymphony init failed: {ex.Message}");
            return 1;
        }
    }

    private static FileAction PlanFileAction(string path, InitConflictPolicy policy, bool nonInteractive)
    {
        if (!File.Exists(path))
            return FileAction.Create;

        return policy switch
        {
            InitConflictPolicy.Skip => FileAction.Skip,
            InitConflictPolicy.Overwrite => FileAction.Overwrite,
            InitConflictPolicy.Abort => FileAction.Abort,
            InitConflictPolicy.Prompt => nonInteractive ? FileAction.Skip : PromptFileAction(path),
            _ => FileAction.Skip
        };
    }

    private static FileAction PromptFileAction(string path)
    {
        // ht: Simple prompt - in production this would use proper console I/O
        Console.Write($"{path} already exists. Overwrite? [y/N] ");
        var response = Console.ReadLine()?.Trim().ToLowerInvariant();
        return response == "y" || response == "yes" ? FileAction.Overwrite : FileAction.Skip;
    }

    private static string GenerateDefaultConfig(string? llmModel, string? llmBaseUrl)
    {
        var model = llmModel ?? DefaultLlmModel;
        var baseUrl = llmBaseUrl ?? DefaultLlmBaseUrl;

        var config = new Dictionary<string, object?>
        {
            ["llm"] = new Dictionary<string, object?>
            {
                ["model"] = model,
                ["base_url"] = baseUrl
            },
            ["linear"] = new Dictionary<string, object?>
            {
                ["api_key"] = "${LINEAR_API_KEY}"
            },
            ["workspace"] = new Dictionary<string, object?>
            {
                ["root"] = "./workspaces"
            }
        };

        var serializer = new SerializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .Build();
        return serializer.Serialize(config);
    }

    private static string GenerateWorkflowTemplate(string? linearProjectSlug)
    {
        var slug = linearProjectSlug ?? "\"YOUR-PROJECT-SLUG\"";
        var sb = new StringBuilder();
        sb.AppendLine("# OpenSymphony Workflow");
        sb.AppendLine();
        sb.AppendLine("This file defines the workflow for this repository.");
        sb.AppendLine();
        sb.AppendLine("## Project");
        sb.AppendLine($"- Linear Project: {slug}");
        sb.AppendLine();
        sb.AppendLine("## Environment");
        sb.AppendLine("```bash");
        sb.AppendLine("# Export your Linear API key");
        sb.AppendLine("export LINEAR_API_KEY=\"your-api-key-here\"");
        sb.AppendLine();
        sb.AppendLine("# Export your LLM API key");
        sb.AppendLine("export LLM_API_KEY=\"your-llm-api-key-here\"");
        sb.AppendLine("```");
        return sb.ToString();
    }

    private record InitArgs(
        bool NonInteractive,
        bool AiPrReview,
        string? LinearProjectSlug,
        InitConflictPolicy? ConflictPolicy,
        bool CommitAndPush,
        bool ConfigureGithub,
        string? LlmModel,
        string? LlmBaseUrl
    )
    {
        public void Validate()
        {
            // ht: Add validation logic as needed
        }
    }

    private enum InitConflictPolicy
    {
        Skip,
        Overwrite,
        Abort,
        Prompt
    }

    private enum FileAction
    {
        Create,
        Overwrite,
        Skip,
        Abort
    }

    private class InitCommandException : Exception
    {
        public InitCommandException(string message) : base(message) { }
    }
}