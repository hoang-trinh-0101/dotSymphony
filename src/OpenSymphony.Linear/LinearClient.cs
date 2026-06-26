using System.Collections;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenSymphony.Domain;

namespace OpenSymphony.Linear;

// ht: Port of older/crates/opensymphony-linear/src/client.rs.

internal static class LinearJsonOptions
{
    public static JsonSerializerOptions Default { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
}

public sealed class RetryPolicy
{
    public int MaxAttempts { get; set; } = 3;
    public TimeSpan InitialBackoff { get; set; } = TimeSpan.FromMilliseconds(250);
    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromSeconds(2);

    public static RetryPolicy Default => new();
}

public sealed class LinearConfig
{
    public string ApiKey { get; set; } = "";
    public string BaseUrl { get; set; } = "https://api.linear.app/graphql";
    public string ProjectSlug { get; set; } = "";
    public List<string> ActiveStates { get; set; } = new();
    public List<string> TerminalStates { get; set; } = new();
    public int PageSize { get; set; } = 50;
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public RetryPolicy RetryPolicy { get; set; } = RetryPolicy.Default;

    public LinearConfig(string apiKey, string projectSlug)
    {
        ApiKey = apiKey;
        ProjectSlug = projectSlug;
    }
}

public sealed class WorkpadComment
{
    public string Id { get; set; } = "";
    public string Body { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class LinearProjectOverview
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string SlugId { get; set; } = "";
    public string Url { get; set; } = "";
    public string? Content { get; set; }
}

public sealed class LinearMilestoneMutationResult
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? TargetDate { get; set; }
    public double? SortOrder { get; set; }
    public string ProjectId { get; set; } = "";
    public string ProjectSlugId { get; set; } = "";
}

public sealed class LinearIssueMutationResult
{
    public string Id { get; set; } = "";
    public string Identifier { get; set; } = "";
    public string? Url { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public double? Priority { get; set; }
    public double? Estimate { get; set; }
    public string StateId { get; set; } = "";
    public string StateName { get; set; } = "";
    public string StateKind { get; set; } = "";
    public string? ProjectId { get; set; }
    public string? ProjectSlugId { get; set; }
    public string? ProjectMilestoneId { get; set; }
    public string? ProjectMilestoneName { get; set; }
    public string? ParentId { get; set; }
    public string? ParentIdentifier { get; set; }
    public string? AssigneeId { get; set; }
    public string? AssigneeName { get; set; }
    public string? AssigneeEmail { get; set; }
    public List<string> LabelNames { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class LinearCommentMutationResult
{
    public string Id { get; set; } = "";
    public string Body { get; set; } = "";
    public string? Url { get; set; }
    public string IssueId { get; set; } = "";
    public string IssueIdentifier { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class LinearIssueRelationMutationResult
{
    public string Id { get; set; } = "";
    public string RelationType { get; set; } = "";
    public string IssueId { get; set; } = "";
    public string IssueIdentifier { get; set; } = "";
    public string RelatedIssueId { get; set; } = "";
    public string RelatedIssueIdentifier { get; set; } = "";
}

public sealed class LinearClient
{
    private const string DefaultBaseUrl = "https://api.linear.app/graphql";
    private const int DefaultPageSize = 50;
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(30);
    private const int MaxInitialRelationPageSize = 10;
    private const int MaxInitialLabelPageSize = 10;

    private readonly HttpClient _http;
    private readonly LinearConfig _config;

    public LinearClient(LinearConfig config) : this(config, null) { }

    // ht: internal constructor for test injection of HttpMessageHandler.
    internal LinearClient(LinearConfig config, HttpMessageHandler? handler)
    {
        if (string.IsNullOrWhiteSpace(config.BaseUrl))
            config.BaseUrl = DefaultBaseUrl;
        if (config.PageSize == 0)
            config.PageSize = DefaultPageSize;
        if (config.RequestTimeout == TimeSpan.Zero)
            config.RequestTimeout = DefaultRequestTimeout;
        if (config.RetryPolicy.MaxAttempts == 0)
            config.RetryPolicy.MaxAttempts = 1;
        if (config.RetryPolicy.InitialBackoff == TimeSpan.Zero)
            config.RetryPolicy.InitialBackoff = TimeSpan.FromMilliseconds(1);
        if (config.RetryPolicy.MaxBackoff < config.RetryPolicy.InitialBackoff)
            config.RetryPolicy.MaxBackoff = config.RetryPolicy.InitialBackoff;

        config.ApiKey = NormalizeRequiredString("LINEAR_API_KEY", config.ApiKey);
        config.ProjectSlug = NormalizeRequiredString("tracker.project_slug", config.ProjectSlug);
        config.ActiveStates = NormalizeRequiredStateNames("tracker.active_states", config.ActiveStates);
        config.TerminalStates = NormalizeRequiredStateNames("tracker.terminal_states", config.TerminalStates);

        _http = handler is null
            ? new HttpClient { Timeout = config.RequestTimeout }
            : new HttpClient(handler) { Timeout = config.RequestTimeout };
        _config = config;
    }

    public async Task<List<TrackerIssue>> CandidateIssues()
        => await IssuesByStateNames(_config.ActiveStates);

    public async Task<List<TrackerIssue>> TerminalIssues()
        => await IssuesByStateNamesWithArchived(_config.TerminalStates, true);

    public Task<List<TrackerIssue>> IssuesByStateNames(IEnumerable<string> stateNames)
        => IssuesByStateNamesWithArchived(stateNames, false);

    public async Task<List<TrackerIssue>> IssuesByIdentifiers(IEnumerable<string> identifiers)
    {
        var ids = NormalizeStrings(identifiers);
        if (ids.Count == 0) return new List<TrackerIssue>();

        var issues = new List<TrackerIssue>();
        var missingIssueIds = new List<string>();

        foreach (var identifier in ids)
        {
            var variables = new
            {
                identifier,
                relationFirst = Math.Min(_config.PageSize, MaxInitialRelationPageSize),
                labelFirst = Math.Min(_config.PageSize, MaxInitialLabelPageSize),
            };
            var response = await ExecuteGraphql<IssueByIdentifierData>(GraphqlQueries.IssueByIdentifierQuery, variables);
            if (response.Issue is null)
            {
                missingIssueIds.Add(identifier);
                continue;
            }
            var issue = Normalize.NormalizeIssue(await ExpandIssue(response.Issue));
            if (!issue.Identifier.Equals(identifier, StringComparison.OrdinalIgnoreCase))
            {
                throw LinearError.InvalidResponse($"Linear issue lookup for {identifier} returned {issue.Identifier}");
            }
            issues.Add(issue);
        }

        if (missingIssueIds.Count > 0)
            throw LinearError.MissingIssueIds(missingIssueIds);
        return issues;
    }

    public async Task<List<TrackerIssue>> ProjectIssuesByIdentifiers(IEnumerable<string> identifiers)
    {
        var ids = NormalizeStrings(identifiers);
        if (ids.Count == 0) return new List<TrackerIssue>();

        var requestedKeys = ids.Select(i => i.ToUpperInvariant()).ToHashSet();
        var projectIssues = await ProjectIssues(false);
        var issuesByIdentifier = new Dictionary<string, TrackerIssue>();
        foreach (var issue in projectIssues)
        {
            var key = issue.Identifier.ToUpperInvariant();
            if (requestedKeys.Contains(key))
                issuesByIdentifier[key] = issue;
        }

        var issues = new List<TrackerIssue>();
        var missingIssueIds = new List<string>();
        foreach (var identifier in ids)
        {
            var key = identifier.ToUpperInvariant();
            if (issuesByIdentifier.TryGetValue(key, out var issue))
            {
                issues.Add(issue);
                issuesByIdentifier.Remove(key);
            }
            else
            {
                missingIssueIds.Add(identifier);
            }
        }

        if (missingIssueIds.Count > 0)
            throw LinearError.MissingIssueIds(missingIssueIds);
        return issues;
    }

    private async Task<List<TrackerIssue>> ProjectIssues(bool includeArchived)
    {
        string? after = null;
        var issues = new List<TrackerIssue>();

        while (true)
        {
            var variables = new
            {
                projectSlug = _config.ProjectSlug,
                includeArchived,
                first = _config.PageSize,
                after,
                relationFirst = Math.Min(_config.PageSize, MaxInitialRelationPageSize),
                labelFirst = Math.Min(_config.PageSize, MaxInitialLabelPageSize),
            };
            var response = await ExecuteGraphql<ProjectIssuesData>(GraphqlQueries.ProjectIssuesQuery, variables);

            var pageInfo = response.Issues.PageInfo;
            foreach (var node in response.Issues.Nodes)
                issues.Add(Normalize.NormalizeIssue(await ExpandIssue(node)));

            if (!pageInfo.HasNextPage) return issues;
            after = pageInfo.EndCursor ?? throw LinearError.InvalidResponse(
                "Linear project issues page indicated a next page without an end cursor");
        }
    }

    private async Task<List<TrackerIssue>> IssuesByStateNamesWithArchived(IEnumerable<string> stateNames, bool includeArchived)
    {
        var states = NormalizeStrings(stateNames);
        if (states.Count == 0) return new List<TrackerIssue>();

        string? after = null;
        var issues = new List<TrackerIssue>();

        while (true)
        {
            var variables = new
            {
                projectSlug = _config.ProjectSlug,
                stateNames = states,
                includeArchived,
                first = _config.PageSize,
                after,
                relationFirst = Math.Min(_config.PageSize, MaxInitialRelationPageSize),
                labelFirst = Math.Min(_config.PageSize, MaxInitialLabelPageSize),
            };
            var response = await ExecuteGraphql<IssuesByStateData>(GraphqlQueries.IssuesByStateQuery, variables);

            var pageInfo = response.Issues.PageInfo;
            foreach (var node in response.Issues.Nodes)
                issues.Add(Normalize.NormalizeIssue(await ExpandIssue(node)));

            if (!pageInfo.HasNextPage) return issues;
            after = pageInfo.EndCursor ?? throw LinearError.InvalidResponse(
                "Linear issues page indicated a next page without an end cursor");
        }
    }

    public async Task<List<TrackerIssueStateSnapshot>> IssueStatesByIds(IEnumerable<string> issueIds)
    {
        var ids = NormalizeStrings(issueIds);
        if (ids.Count == 0) return new List<TrackerIssueStateSnapshot>();

        string? after = null;
        var snapshots = new List<TrackerIssueStateSnapshot>();

        while (true)
        {
            var variables = new
            {
                projectSlug = _config.ProjectSlug,
                issueIds = ids,
                first = _config.PageSize,
                after,
            };
            var response = await ExecuteGraphql<IssueStatesByIdsData>(GraphqlQueries.IssueStatesByIdsQuery, variables);

            var pageInfo = response.Issues.PageInfo;
            foreach (var node in response.Issues.Nodes)
                snapshots.Add(Normalize.NormalizeIssueState(node));

            if (!pageInfo.HasNextPage) return snapshots;
            after = pageInfo.EndCursor ?? throw LinearError.InvalidResponse(
                "Linear issue-state page indicated a next page without an end cursor");
        }
    }

    public async Task<WorkpadComment?> FetchWorkpadComment(string issueId)
    {
        issueId = NormalizeRequiredString("issue_id", issueId);
        string? after = null;
        WorkpadComment? latest = null;

        while (true)
        {
            var variables = new
            {
                issueId,
                first = _config.PageSize,
                after,
            };
            var response = await ExecuteGraphql<IssueCommentsData>(GraphqlQueries.IssueCommentsQuery, variables);
            var issue = response.Issue ?? throw LinearError.MissingIssueIds([issueId]);
            if (issue.Id != issueId)
                throw LinearError.InvalidResponse($"Linear comments page returned mismatched issue ID {issue.Id} for {issueId}");

            foreach (var comment in issue.Comments.Nodes)
            {
                if (comment.ResolvedAt is not null || !ContainsWorkpadMarker(comment.Body))
                    continue;

                var candidate = new WorkpadComment
                {
                    Id = comment.Id,
                    Body = comment.Body,
                    UpdatedAt = comment.UpdatedAt,
                };
                if (latest is null || candidate.UpdatedAt > latest.UpdatedAt)
                    latest = candidate;
            }

            if (!issue.Comments.PageInfo.HasNextPage) return latest;
            after = issue.Comments.PageInfo.EndCursor ?? throw LinearError.InvalidResponse(
                $"Linear comments page for issue {issueId} indicated a next page without an end cursor");
        }
    }

    public async Task ArchiveIssue(string issueIdOrIdentifier)
    {
        issueIdOrIdentifier = NormalizeRequiredString("issue_id_or_identifier", issueIdOrIdentifier);
        var variables = new { id = issueIdOrIdentifier, trash = false };
        var response = await ExecuteGraphql<IssueArchiveData>(GraphqlQueries.IssueArchiveMutation, variables);
        if (!response.IssueArchive.Success)
            throw LinearError.InvalidResponse("Linear issueArchive returned success=false");
    }

    public async Task<LinearProjectOverview?> ProjectOverview()
    {
        var variables = new { slug = _config.ProjectSlug };
        var response = await ExecuteGraphql<ProjectBySlugData>(GraphqlQueries.ProjectBySlugQuery, variables);
        var node = response.Projects.Nodes.FirstOrDefault();
        return node is null ? null : new LinearProjectOverview
        {
            Id = node.Id,
            Name = node.Name,
            SlugId = node.SlugId,
            Url = node.Url,
            Content = node.Content,
        };
    }

    public async Task UpdateProjectContent(string projectId, string content)
    {
        var variables = new
        {
            id = NormalizeRequiredString("project_id", projectId),
            content,
        };
        var response = await ExecuteGraphql<ProjectUpdateContentData>(GraphqlQueries.ProjectUpdateContentMutation, variables);
        if (!response.ProjectUpdate.Success)
            throw LinearError.InvalidResponse("Linear projectUpdate returned success=false");
    }

    public async Task<LinearMilestoneMutationResult> CreateProjectMilestone(ProjectMilestoneCreateInput input)
    {
        var variables = new { input };
        var response = await ExecuteGraphql<ProjectMilestoneCreateData>(GraphqlQueries.ProjectMilestoneCreateMutation, variables);
        return MilestoneMutationResult("projectMilestoneCreate", response.ProjectMilestoneCreate);
    }

    public async Task<LinearMilestoneMutationResult> UpdateProjectMilestone(string milestoneId, ProjectMilestoneUpdateInput input)
    {
        var variables = new
        {
            id = NormalizeRequiredString("milestone_id", milestoneId),
            input,
        };
        var response = await ExecuteGraphql<ProjectMilestoneUpdateData>(GraphqlQueries.ProjectMilestoneUpdateMutation, variables);
        return MilestoneMutationResult("projectMilestoneUpdate", response.ProjectMilestoneUpdate);
    }

    private static LinearMilestoneMutationResult MilestoneMutationResult(string operation, ProjectMilestoneMutationPayload payload)
    {
        if (!payload.Success)
            throw LinearError.InvalidResponse($"Linear {operation} returned success=false");
        if (payload.ProjectMilestone is null)
            throw LinearError.InvalidResponse($"Linear {operation} returned success=true without a projectMilestone");
        var node = payload.ProjectMilestone;
        return new LinearMilestoneMutationResult
        {
            Id = node.Id,
            Name = node.Name,
            Description = node.Description,
            TargetDate = node.TargetDate,
            SortOrder = node.SortOrder,
            ProjectId = node.Project.Id,
            ProjectSlugId = node.Project.SlugId,
        };
    }

    public async Task<LinearIssueMutationResult> CreateIssue(IssueCreateInput input)
    {
        ValidateIssueCreateInput(input);
        var variables = new { input };
        var response = await ExecuteGraphql<IssueCreateData>(GraphqlQueries.IssueCreateMutation, variables);
        return IssueMutationResult("issueCreate", response.IssueCreate);
    }

    public async Task<LinearIssueMutationResult> UpdateIssue(string issueId, IssueUpdateInput input)
    {
        var variables = new
        {
            id = NormalizeRequiredString("issue_id", issueId),
            input,
        };
        var response = await ExecuteGraphql<IssueUpdateData>(GraphqlQueries.IssueUpdateMutation, variables);
        return IssueMutationResult("issueUpdate", response.IssueUpdate);
    }

    private static LinearIssueMutationResult IssueMutationResult(string operation, IssueMutationPayload payload)
    {
        if (!payload.Success)
            throw LinearError.InvalidResponse($"Linear {operation} returned success=false");
        if (payload.Issue is null)
            throw LinearError.InvalidResponse($"Linear {operation} returned success=true without an issue");
        var node = payload.Issue;
        return new LinearIssueMutationResult
        {
            Id = node.Id,
            Identifier = node.Identifier,
            Url = node.Url,
            Title = node.Title,
            Description = node.Description,
            Priority = node.Priority,
            Estimate = node.Estimate,
            StateId = node.State.Id,
            StateName = node.State.Name,
            StateKind = node.State.Kind,
            ProjectId = node.Project?.Id,
            ProjectSlugId = node.Project?.SlugId,
            ProjectMilestoneId = node.ProjectMilestone?.Id,
            ProjectMilestoneName = node.ProjectMilestone?.Name,
            ParentId = node.Parent?.Id,
            ParentIdentifier = node.Parent?.Identifier,
            AssigneeId = node.Assignee?.Id,
            AssigneeName = node.Assignee?.Name,
            AssigneeEmail = node.Assignee?.Email,
            LabelNames = node.Labels.Nodes.Select(l => l.Name).ToList(),
            CreatedAt = node.CreatedAt,
            UpdatedAt = node.UpdatedAt,
        };
    }

    private static void ValidateIssueCreateInput(IssueCreateInput input)
    {
        NormalizeRequiredString("issue.team_id", input.TeamId);
        NormalizeRequiredString("issue.title", input.Title);
    }

    public async Task<LinearCommentMutationResult> CreateComment(string issueId, string body)
    {
        var input = new CommentCreateInput
        {
            IssueId = NormalizeRequiredString("issue_id", issueId),
            Body = NormalizeRequiredString("comment.body", body),
        };
        var variables = new { input };
        var response = await ExecuteGraphql<CommentCreateData>(GraphqlQueries.CommentCreateMutation, variables);
        if (!response.CommentCreate.Success)
            throw LinearError.InvalidResponse("Linear commentCreate returned success=false");
        if (response.CommentCreate.Comment is null)
            throw LinearError.InvalidResponse("Linear commentCreate returned success=true without a comment");
        var node = response.CommentCreate.Comment;
        return new LinearCommentMutationResult
        {
            Id = node.Id,
            Body = node.Body,
            Url = node.Url,
            IssueId = node.Issue.Id,
            IssueIdentifier = node.Issue.Identifier,
            CreatedAt = node.CreatedAt,
            UpdatedAt = node.UpdatedAt,
        };
    }

    public async Task<LinearIssueRelationMutationResult> CreateIssueRelation(string issueId, string relatedIssueId, string relationType)
    {
        var input = new IssueRelationCreateInput
        {
            IssueId = NormalizeRequiredString("issue_id", issueId),
            RelatedIssueId = NormalizeRequiredString("related_issue_id", relatedIssueId),
            RelationType = NormalizeRequiredString("relation_type", relationType),
        };
        var variables = new { input };
        var response = await ExecuteGraphql<IssueRelationCreateData>(GraphqlQueries.IssueRelationCreateMutation, variables);
        if (!response.IssueRelationCreate.Success)
            throw LinearError.InvalidResponse("Linear issueRelationCreate returned success=false");
        if (response.IssueRelationCreate.IssueRelation is null)
            throw LinearError.InvalidResponse("Linear issueRelationCreate returned success=true without an issueRelation");
        var node = response.IssueRelationCreate.IssueRelation;
        return new LinearIssueRelationMutationResult
        {
            Id = node.Id,
            RelationType = node.RelationType,
            IssueId = node.Issue.Id,
            IssueIdentifier = node.Issue.Identifier,
            RelatedIssueId = node.RelatedIssue.Id,
            RelatedIssueIdentifier = node.RelatedIssue.Identifier,
        };
    }

    private async Task<LinearIssueNode> ExpandIssue(LinearIssueNode issue)
    {
        issue.Labels = await LoadAllLabels(issue.Id, issue.Labels);
        issue.InverseRelations = await LoadAllInverseRelations(issue.Id, issue.InverseRelations);
        return issue;
    }

    private async Task<LinearLabelConnection> LoadAllLabels(string issueId, LinearLabelConnection connection)
    {
        var after = connection.PageInfo.EndCursor;

        while (connection.PageInfo.HasNextPage)
        {
            if (after is null)
                throw LinearError.InvalidResponse($"Linear labels page for issue {issueId} indicated a next page without an end cursor");
            var variables = new { issueId, first = _config.PageSize, after };
            var response = await ExecuteGraphql<IssueLabelsData>(GraphqlQueries.IssueLabelsQuery, variables);
            var issue = response.Issue ?? throw LinearError.MissingIssueIds([issueId]);
            if (issue.Id != issueId)
                throw LinearError.InvalidResponse($"Linear labels page returned mismatched issue ID {issue.Id} for {issueId}");

            connection.Nodes.AddRange(issue.Labels.Nodes);
            connection.PageInfo = issue.Labels.PageInfo;
            after = connection.PageInfo.EndCursor;
        }

        return connection;
    }

    private async Task<LinearRelationConnection> LoadAllInverseRelations(string issueId, LinearRelationConnection connection)
    {
        var after = connection.PageInfo.EndCursor;

        while (connection.PageInfo.HasNextPage)
        {
            if (after is null)
                throw LinearError.InvalidResponse($"Linear inverseRelations page for issue {issueId} indicated a next page without an end cursor");
            var variables = new { issueId, first = _config.PageSize, after };
            var response = await ExecuteGraphql<IssueInverseRelationsData>(GraphqlQueries.IssueInverseRelationsQuery, variables);
            var issue = response.Issue ?? throw LinearError.MissingIssueIds([issueId]);
            if (issue.Id != issueId)
                throw LinearError.InvalidResponse($"Linear inverseRelations page returned mismatched issue ID {issue.Id} for {issueId}");

            connection.Nodes.AddRange(issue.InverseRelations.Nodes);
            connection.PageInfo = issue.InverseRelations.PageInfo;
            after = connection.PageInfo.EndCursor;
        }

        return connection;
    }

    internal async Task<T> ExecuteGraphql<T>(string query, object variables)
    {
        var bodyObj = new { query, variables };
        var bodyJson = JsonSerializer.Serialize(bodyObj, LinearJsonOptions.Default);
        var authorization = _config.ApiKey;
        var operation = GraphqlOperationName(query);
        var attempt = 1;

        while (true)
        {
            HttpResponseMessage response;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, _config.BaseUrl);
                request.Headers.TryAddWithoutValidation("Authorization", authorization);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");
                response = await _http.SendAsync(request, CancellationToken.None);
            }
            catch (Exception ex) when (ex is TaskCanceledException or OperationCanceledException)
            {
                var error = LinearError.Request(ex.Message, isTimeout: true);
                if (ShouldRetry(error, attempt))
                {
                    await SleepBeforeRetry(error, attempt);
                    attempt++;
                    continue;
                }
                throw error;
            }
            catch (Exception ex)
            {
                var error = LinearError.Request(ex.Message);
                if (ShouldRetry(error, attempt))
                {
                    await SleepBeforeRetry(error, attempt);
                    attempt++;
                    continue;
                }
                throw error;
            }

            using (response)
            {
                var status = response.StatusCode;
                var retryAfter = ParseRetryDelay(response.Headers, response.Content?.Headers);
                var metadata = ResponseMetadataFromHeaders(response.Headers, response.Content?.Headers);
                string payload;
                try
                {
                    payload = await response.Content.ReadAsStringAsync();
                }
                catch (Exception ex)
                {
                    var error = LinearError.ResponseBody(operation, status, metadata, retryAfter, ex.Message);
                    if (ShouldRetry(error, attempt))
                    {
                        await SleepBeforeRetry(error, attempt);
                        attempt++;
                        continue;
                    }
                    throw error;
                }

                if (status == HttpStatusCode.TooManyRequests || (int)status >= 500)
                {
                    var error = LinearError.HttpStatus(status, payload, retryAfter);
                    if (ShouldRetry(error, attempt))
                    {
                        await SleepBeforeRetry(error, attempt);
                        attempt++;
                        continue;
                    }
                    throw error;
                }

                if (DecodeGraphqlErrorResponse(payload, retryAfter) is LinearError graphqlError)
                {
                    if (ShouldRetry(graphqlError, attempt))
                    {
                        await SleepBeforeRetry(graphqlError, attempt);
                        attempt++;
                        continue;
                    }
                    throw graphqlError;
                }

                if (!response.IsSuccessStatusCode)
                {
                    var error = LinearError.HttpStatus(status, payload, retryAfter);
                    if (ShouldRetry(error, attempt))
                    {
                        await SleepBeforeRetry(error, attempt);
                        attempt++;
                        continue;
                    }
                    throw error;
                }

                GraphqlEnvelope<T>? envelope;
                try
                {
                    envelope = JsonSerializer.Deserialize<GraphqlEnvelope<T>>(payload, LinearJsonOptions.Default);
                }
                catch (JsonException ex)
                {
                    throw LinearError.InvalidResponse(
                        $"failed to decode Linear GraphQL response for {operation} after HTTP {(int)status}: {ex.Message} ({metadata}, body_bytes={payload.Length})");
                }

                if (envelope is null)
                    throw LinearError.InvalidResponse(
                        $"failed to decode Linear GraphQL response for {operation} after HTTP {(int)status}: null envelope ({metadata}, body_bytes={payload.Length})");

                if (envelope.Errors is not null)
                {
                    var error = LinearError.FromGraphqlErrorsWithRetryAfter(
                        ConvertGraphqlErrors(envelope.Errors), retryAfter);
                    if (ShouldRetry(error, attempt))
                    {
                        await SleepBeforeRetry(error, attempt);
                        attempt++;
                        continue;
                    }
                    throw error;
                }

                if (envelope.Data is null)
                    throw LinearError.InvalidResponse(
                        $"Linear GraphQL response for {operation} omitted both data and errors ({metadata}, body_bytes={payload.Length})");

                return envelope.Data;
            }
        }
    }

    public async Task<SchemaDriftReport> CheckSchemaDrift()
    {
        var required = RequiredFields.List;
        var missing = new List<SchemaDriftViolation>();
        var typeNames = required.Select(f => f.TypeName).Distinct().ToHashSet();
        var remoteFields = new Dictionary<string, HashSet<string>>();

        foreach (var typeName in typeNames)
        {
            var fields = await IntrospectType(typeName);
            remoteFields[typeName] = fields.ToHashSet();
        }

        var checkedAt = DateTimeOffset.UtcNow;

        foreach (var req in required)
        {
            if (remoteFields.TryGetValue(req.TypeName, out var fieldSet) && fieldSet.Contains(req.FieldName))
                continue;

            missing.Add(new SchemaDriftViolation
            {
                TypeName = req.TypeName,
                FieldName = req.FieldName,
                Critical = req.Critical,
                Remediation = $"Field `{req.FieldName}` on type `{req.TypeName}` missing in remote Linear schema. Check Linear API changelog, update graphql.rs / normalize.rs, or remove from required_fields().",
            });
        }

        return new SchemaDriftReport
        {
            IsCompatible = missing.Count == 0,
            MissingFields = missing,
            CheckedAt = checkedAt,
        };
    }

    internal async Task<List<string>> IntrospectType(string typeName)
    {
        var response = await ExecuteGraphql<JsonElement>(GraphqlQueries.IntrospectTypeQuery, new { typeName });

        if (!response.TryGetProperty("data", out var dataEl) || !dataEl.TryGetProperty("__type", out var typeNode) || typeNode.ValueKind == JsonValueKind.Null)
            throw LinearError.InvalidResponse($"Introspection returned null for type `{typeName}` (type may not exist or was renamed)");

        if (!typeNode.TryGetProperty("fields", out var fieldsEl) || fieldsEl.ValueKind != JsonValueKind.Array)
            throw LinearError.InvalidResponse($"Introspection for type `{typeName}` returned no fields");

        var names = new List<string>();
        foreach (var field in fieldsEl.EnumerateArray())
        {
            if (field.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
                names.Add(nameEl.GetString()!);
        }
        return names;
    }

    private bool ShouldRetry(LinearError error, int attempt)
    {
        if (attempt >= _config.RetryPolicy.MaxAttempts) return false;

        return error switch
        {
            LinearError.RequestError => true,
            LinearError.ResponseBodyError => true,
            LinearError.HttpStatusError http => http.Status == HttpStatusCode.TooManyRequests || (int)http.Status >= 500,
            LinearError.GraphqlErrorVariant => error.IsRateLimited(),
            LinearError.MissingIssueIdsError => false,
            LinearError.InvalidConfigurationError => false,
            LinearError.InvalidResponseError => false,
            _ => false,
        };
    }

    private async Task SleepBeforeRetry(LinearError error, int attempt)
    {
        var delay = error.RetryAfter() ?? ExponentialBackoff(attempt);
        await Task.Delay(delay);
    }

    private TimeSpan ExponentialBackoff(int attempt)
    {
        var delay = _config.RetryPolicy.InitialBackoff;
        for (var i = 1; i < attempt; i++)
        {
            var next = delay * 2;
            if (next <= _config.RetryPolicy.MaxBackoff)
                delay = next;
            else
                return _config.RetryPolicy.MaxBackoff;
        }
        return delay;
    }

    // ht: Static helpers mirroring Rust free functions.

    internal static List<GraphqlError> ConvertGraphqlErrors(List<GraphqlErrorPayload> errors)
        => errors.Select(e => new GraphqlError
        {
            Message = e.Message,
            Code = e.Extensions?.Code,
        }).ToList();

    internal static LinearError? DecodeGraphqlErrorResponse(string payload, TimeSpan? retryAfter)
    {
        try
        {
            var envelope = JsonSerializer.Deserialize<GraphqlEnvelope<JsonElement>>(payload, LinearJsonOptions.Default);
            if (envelope?.Errors is null) return null;
            return LinearError.FromGraphqlErrorsWithRetryAfter(ConvertGraphqlErrors(envelope.Errors), retryAfter);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static string GraphqlOperationName(string query)
    {
        var tokens = query.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return "<anonymous>";
        var first = tokens[0];
        if (first is not ("query" or "mutation" or "subscription"))
            return "<anonymous>";
        if (tokens.Length < 2) return "<anonymous>";
        var token = tokens[1];
        var name = token.Split('(', '{')[0];
        return string.IsNullOrEmpty(name) ? "<anonymous>" : name;
    }

    internal static ResponseMetadata ResponseMetadataFromHeaders(HttpResponseHeaders headers, HttpContentHeaders? contentHeaders)
    {
        string? GetHeader(string name)
        {
            if (contentHeaders is not null && contentHeaders.TryGetValues(name, out var contentValues))
                return contentValues.FirstOrDefault();
            if (headers.TryGetValues(name, out var values))
                return values.FirstOrDefault();
            return null;
        }

        return new ResponseMetadata
        {
            ContentType = GetHeader("Content-Type"),
            ContentLength = GetHeader("Content-Length"),
            ContentEncoding = GetHeader("Content-Encoding"),
        };
    }

    internal static List<string> NormalizeStrings(IEnumerable<string> values)
    {
        var normalized = new List<string>();
        foreach (var value in values)
        {
            var trimmed = value.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;
            if (!normalized.Contains(trimmed))
                normalized.Add(trimmed);
        }
        return normalized;
    }

    internal static List<string> NormalizeRequiredStateNames(string fieldName, IEnumerable<string> values)
    {
        var normalized = NormalizeStrings(values);
        if (normalized.Count == 0)
            throw LinearError.InvalidConfiguration($"workflow {fieldName} must contain at least one non-empty state name");
        return normalized;
    }

    internal static string NormalizeRequiredString(string fieldName, string value)
    {
        var normalized = value.Trim();
        if (string.IsNullOrEmpty(normalized))
            throw LinearError.InvalidConfiguration($"{fieldName} must be a non-empty string");
        return normalized;
    }

    internal static bool ContainsWorkpadMarker(string body)
    {
        foreach (var line in body.Split('\n'))
        {
            if (line.TrimStart().StartsWith("## Agent Harness Workpad"))
                return true;
        }
        return false;
    }

    internal static TimeSpan? ParseRetryAfter(string? headerValue)
    {
        if (headerValue is null) return null;
        if (int.TryParse(headerValue.Trim(), out var seconds))
            return TimeSpan.FromSeconds(seconds);
        return null;
    }

    internal static TimeSpan? ParseRetryDelay(HttpResponseHeaders headers, HttpContentHeaders? contentHeaders)
    {
        var reset = ParseRateLimitReset(headers, contentHeaders, DateTimeOffset.UtcNow);
        if (reset is not null) return reset;

        string? retryAfterValue = null;
        if (headers.TryGetValues("Retry-After", out var values))
            retryAfterValue = values.FirstOrDefault();
        return ParseRetryAfter(retryAfterValue);
    }

    // ht: Exposed for unit testing with a fixed "now" timestamp.
    internal static TimeSpan? ParseRateLimitReset(HttpResponseHeaders headers, HttpContentHeaders? contentHeaders, DateTimeOffset now)
    {
        var resetHeaders = new[] { "x-ratelimit-requests-reset", "x-ratelimit-endpoint-requests-reset", "x-ratelimit-complexity-reset" };

        var nowMs = now.ToUnixTimeMilliseconds();
        ulong? latestResetMs = null;

        foreach (var headerName in resetHeaders)
        {
            if (headers.TryGetValues(headerName, out var values))
            {
                foreach (var value in values)
                {
                    if (ulong.TryParse(value.Trim(), out var parsed))
                    {
                        if (latestResetMs is null || parsed > latestResetMs)
                            latestResetMs = parsed;
                    }
                }
            }
        }

        if (latestResetMs is null) return null;
        var delayMs = latestResetMs.Value > (ulong)nowMs ? latestResetMs.Value - (ulong)nowMs : 0;
        return TimeSpan.FromMilliseconds(delayMs);
    }
}
