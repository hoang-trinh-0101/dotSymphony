using System.Reflection;
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

        // ht: Fluid 2.31 is lenient (silently ignores unknown filters, renders missing members as empty).
        // Validate against the parsed AST to match Rust's strict template rendering failure modes.
        var filters = new HashSet<string>();
        var memberChains = new List<List<string>>();
        CollectTemplateReferences(template, filters, memberChains);

        foreach (var f in filters)
        {
            if (!context.Options.Filters.TryGetValue(f, out _))
            {
                return Result<string, PromptTemplateError>.Err(
                    new PromptTemplateParse(new Exception($"unknown filter: {f}")));
            }
        }

        if (issueElement.ValueKind == JsonValueKind.Object)
        {
            var issueProps = issueElement.EnumerateObject().Select(p => p.Name).ToHashSet();
            foreach (var chain in memberChains)
            {
                // ht: only flag direct issue.X member access; deeper chains (issue.X.Y) check X only.
                if (chain.Count >= 2 && chain[0] == "issue" && !issueProps.Contains(chain[1]))
                {
                    return Result<string, PromptTemplateError>.Err(
                        new PromptTemplateRender(new Exception($"unknown member: issue.{chain[1]}")));
                }
            }
        }

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

    // ht: walk the Fluid AST via reflection to collect filter names and issue member-access chains.
    // Fluid 2.31 doesn't expose a public visitor, so reflect over backing fields.
    private static void CollectTemplateReferences(object? node, HashSet<string> filters, List<List<string>> memberChains)
    {
        if (node is null) return;
        var type = node.GetType();

        if (type.Name == "FilterExpression")
        {
            var nameField = type.GetField("<Name>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
            if (nameField?.GetValue(node) is string name)
                filters.Add(name);
        }

        if (type.Name == "MemberExpression")
        {
            var segsField = type.GetField("_segments", BindingFlags.NonPublic | BindingFlags.Instance);
            if (segsField?.GetValue(node) is Array segs)
            {
                var chain = new List<string>();
                foreach (var seg in segs)
                {
                    var idProp = seg?.GetType().GetProperty("Identifier");
                    if (idProp?.GetValue(seg) is string id)
                        chain.Add(id);
                }
                if (chain.Count > 0)
                    memberChains.Add(chain);
            }
        }

        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            var val = field.GetValue(node);
            if (val is null) continue;
            if (val is Array arr)
            {
                foreach (var item in arr)
                    CollectTemplateReferences(item, filters, memberChains);
            }
            else if (val is System.Collections.IEnumerable en && val is not string)
            {
                foreach (var item in en)
                    CollectTemplateReferences(item, filters, memberChains);
            }
            else if (field.FieldType.IsClass && field.FieldType.Namespace == "Fluid.Ast")
            {
                CollectTemplateReferences(val, filters, memberChains);
            }
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
