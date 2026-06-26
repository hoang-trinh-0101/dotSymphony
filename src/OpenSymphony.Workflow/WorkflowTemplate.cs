using System.Text.Json;
using Fluid;
using OpenSymphony.Domain;

namespace OpenSymphony.Workflow;

public static class WorkflowTemplate
{
    private static readonly FluidParser _parser = new FluidParser();

    public static Result<string, PromptTemplateError> RenderPrompt<T>(string templateSource, T issue, uint? attempt)
    {
        if (!_parser.TryParse(templateSource, out var template, out var parseError))
        {
            return Result<string, PromptTemplateError>.Err(new PromptTemplateParse(new Exception(parseError)));
        }

        // ht: serialize issue to JsonElement then convert to plain object for Fluid reflection.
        var issueElement = JsonSerializer.SerializeToElement(issue);
        var context = new TemplateContext();
        context.SetValue("issue", JsonElementToObject(issueElement));

        if (attempt.HasValue)
            context.SetValue("attempt", (long)attempt.Value);
        else
            context.SetValue("attempt", null);

        try
        {
            var rendered = template.Render(context);
            return Result<string, PromptTemplateError>.Ok(rendered);
        }
        catch (Exception ex)
        {
            return Result<string, PromptTemplateError>.Err(new PromptTemplateRender(ex));
        }
    }

    // ht: convert JsonElement to plain CLR objects that Fluid can reflect over natively.
    private static object? JsonElementToObject(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? "",
            JsonValueKind.Number => element.TryGetDecimal(out var d) ? (object)d : element.GetInt64(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array => element.EnumerateArray().Select(JsonElementToObject).ToList(),
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(p => p.Name, p => JsonElementToObject(p.Value)),
            _ => null,
        };
    }
}
