using OpenSymphony.Domain;

namespace OpenSymphony.Tui;

public partial class TuiState
{
    public string RenderText(int width, int height)
    {
        if (width == 0 || height == 0)
            return string.Empty;

        var (bodyRows, timelineRows) = SectionLayout(height);
        var lines = new List<string>();
        var snapshot = LatestSnapshot;
        var issueCount = snapshot?.Snapshot.Issues.Count ?? 0;
        var sequence = snapshot?.Sequence ?? 0;
        var generated = snapshot != null ? FormatTimestamp(snapshot.Snapshot.GeneratedAt) : "--:--:--";

        var headerParts = new List<string>
        {
            "OpenSymphony",
            DaemonStatusSummary(snapshot),
            AgentServerStatusSummary(snapshot),
            ConnectionStatusSummary(),
            $"seq={sequence}",
            $"focus={Focus.Label()}",
            $"bottom={TimelineMode.Label()}",
            $"issues={issueCount}",
            $"updated={generated}",
            "q quit  tab focus  shift-tab back  enter diff  e toggle"
        };

        lines.Add(Fit(string.Join(" | ", headerParts), width));
        lines.Add(new string('=', width));

        if (width >= 80)
        {
            var leftWidth = Math.Max(50, width * 3 / 5);
            var rightWidth = Math.Max(0, width - leftWidth - 3);
            var left = IssueLines(leftWidth, bodyRows);
            var right = DetailLines(rightWidth, bodyRows);
            lines.AddRange(FitSection(TwoColumnBlock(left, right, leftWidth, rightWidth), bodyRows, width));
        }
        else
        {
            var (issueRows, detailRows) = StackedBodyLayout(bodyRows);
            lines.AddRange(FitSection(IssueLines(width, issueRows), issueRows, width));
            if (detailRows > 0)
            {
                lines.Add(new string('-', width));
                lines.AddRange(FitSection(DetailLines(width, detailRows), detailRows, width));
            }
        }

        if (timelineRows > 0)
        {
            lines.Add(new string('=', width));
            lines.AddRange(FitSection(TimelineLines(width), timelineRows, width));
        }

        while (lines.Count > height)
        {
            lines.RemoveAt(lines.Count - 1);
        }
        while (lines.Count < height)
        {
            lines.Add(new string(' ', width));
        }

        return string.Join("\n", lines);
    }

    private static (int bodyRows, int timelineRows) SectionLayout(int height)
    {
        var timelineRows = Math.Min(MAX_TIMELINE_LINES, Math.Max(MIN_TIMELINE_LINES, height / 6));
        var bodyRows = Math.Max(0, height - timelineRows - 2); // -2 for header lines
        return (bodyRows, timelineRows);
    }

    private static (int issueRows, int detailRows) StackedBodyLayout(int bodyRows)
    {
        var issueRows = Math.Max(4, bodyRows / 2);
        var detailRows = Math.Max(0, bodyRows - issueRows - 1);
        return (issueRows, detailRows);
    }

    private static string DaemonStatusSummary(SnapshotEnvelope? snapshot)
    {
        if (snapshot == null)
            return "daemon=--";
        return $"daemon={snapshot.Snapshot.Daemon.State}";
    }

    private static string AgentServerStatusSummary(SnapshotEnvelope? snapshot)
    {
        if (snapshot == null)
            return "agent=--";
        return $"agent={(snapshot.Snapshot.AgentServer.Reachable ? "up" : "down")}";
    }

    private string ConnectionStatusSummary()
    {
        return $"conn={Connection.Label()}";
    }

    private static string Fit(string text, int width)
    {
        if (text.Length <= width)
            return text.PadRight(width);
        return text.Substring(0, width);
    }

    private static List<string> FitSection(List<string> lines, int maxRows, int width)
    {
        var result = new List<string>();
        foreach (var line in lines.Take(maxRows))
        {
            result.Add(Fit(line, width));
        }
        while (result.Count < maxRows)
        {
            result.Add(new string(' ', width));
        }
        return result;
    }

    private static List<string> TwoColumnBlock(List<string> left, List<string> right, int leftWidth, int rightWidth)
    {
        var result = new List<string>();
        var maxRows = Math.Max(left.Count, right.Count);
        for (int i = 0; i < maxRows; i++)
        {
            var leftLine = i < left.Count ? left[i] : new string(' ', leftWidth);
            var rightLine = i < right.Count ? right[i] : new string(' ', rightWidth);
            result.Add(Fit(leftLine, leftWidth) + " | " + Fit(rightLine, rightWidth));
        }
        return result;
    }

    private List<string> IssueLines(int width, int maxRows)
    {
        var lines = new List<string>
        {
            Fit(PaneTitle("ISSUES", Focus == FocusPane.Issues), width)
        };

        if (LatestSnapshot == null)
        {
            lines.Add(Fit("awaiting first snapshot", width));
            return lines;
        }

        if (LatestSnapshot.Snapshot.Issues.Count == 0)
        {
            lines.Add(Fit("no issues in snapshot", width));
            return lines;
        }

        var (start, end) = IssueWindow(
            LatestSnapshot.Snapshot.Issues.Count,
            SelectedIssue,
            VisibleIssueCount(maxRows));

        for (int i = start; i < end; i++)
        {
            var issue = LatestSnapshot.Snapshot.Issues[i];
            var marker = i == SelectedIssue ? ">" : " ";
            var line = $"{marker} {issue.Identifier} [{issue.RuntimeState} / {issue.TrackerState}] {issue.Title}";
            lines.Add(Fit(line, width));
        }

        return lines;
    }

    private List<string> DetailLines(int width, int maxRows)
    {
        var lines = new List<string>
        {
            Fit(PaneTitle("ISSUE + WORKSPACE DETAIL", Focus == FocusPane.Detail), width)
        };

        var issue = GetSelectedIssue();
        if (issue == null)
        {
            lines.Add(Fit("no selected issue", width));
            return lines;
        }

        lines.Add(Fit($"{issue.Identifier} {issue.Title}", width));
        lines.Add(Fit($"tracker: {issue.TrackerState} | runtime: {issue.RuntimeState} | outcome: {issue.LastOutcome}", width));
        lines.Add(Fit($"branch: {BranchText(issue)}", width));
        lines.Add(Fit($"pr: {PrText(issue)}", width));
        lines.Add(Fit($"last event: {FormatTimestamp(issue.LastEventAt)} | retries: {issue.RetryCount} | blocked: {issue.Blocked}", width));

        if (lines.Count < maxRows)
        {
            lines.Add(new string('-', Math.Min(40, width)));
            var remainingRows = maxRows - lines.Count;

            if (DetailDiffOpen)
            {
                var fileRows = Math.Min(Math.Max(4, remainingRows / 3), remainingRows);
                lines.AddRange(ModifiedFilesLines(width, issue, fileRows));

                var diffRows = maxRows - lines.Count - 1;
                if (diffRows > 0)
                {
                    lines.Add(new string('-', Math.Min(40, width)));
                    lines.AddRange(SelectedDiffLines(width, diffRows));
                }
            }
            else
            {
                var fileRows = Math.Min(Math.Max(4, remainingRows / 2), remainingRows);
                lines.AddRange(ModifiedFilesLines(width, issue, fileRows));

                var conversationRows = maxRows - lines.Count - 1;
                if (conversationRows > 0)
                {
                    lines.Add(new string('-', Math.Min(40, width)));
                    lines.AddRange(ConversationEventsLines(width, issue, conversationRows));
                }
            }
        }

        return lines;
    }

    private List<string> ModifiedFilesLines(int width, ControlPlaneIssueSnapshot issue, int maxRows)
    {
        var lines = new List<string>
        {
            Fit("MODIFIED FILES", width)
        };

        if (maxRows <= 1)
            return lines;

        if (issue.WorkspacePathSuffix == "-")
        {
            lines.Add(Fit("workspace unavailable", width));
            return lines;
        }

        if (!WorkspaceStatus.TryGetValue(issue.Identifier, out var entry) || entry.IsLoading)
        {
            lines.Add(Fit("loading git changes...", width));
            return lines;
        }

        if (entry.Changes == null)
        {
            lines.Add(Fit("loading git changes...", width));
            return lines;
        }

        if (entry.Changes.State == WorkspaceChangeState.Unavailable)
        {
            lines.Add(Fit(entry.Changes.UnavailableReason ?? "changes unavailable", width));
            return lines;
        }

        if (entry.Changes.Summary == null)
        {
            lines.Add(Fit("no changes summary", width));
            return lines;
        }

        lines.Add(Fit(ChangeSummaryLineText(entry.Changes.Summary), width));

        if (entry.Changes.Summary.Files.Count == 0)
        {
            lines.Add(Fit("no modified files", width));
            return lines;
        }

        var visibleRows = Math.Max(0, maxRows - lines.Count);
        if (visibleRows == 0)
            return lines;

        var (start, end) = IssueWindow(
            entry.Changes.Summary.Files.Count,
            SelectedChangedFile,
            visibleRows);

        for (int i = start; i < end; i++)
        {
            var file = entry.Changes.Summary.Files[i];
            lines.Add(ChangeTargetLineText(
                file.DisplayPath,
                file.Additions,
                file.Deletions,
                width,
                i == SelectedChangedFile,
                DetailDiffOpen));
        }

        return lines;
    }

    private List<string> SelectedDiffLines(int width, int maxRows)
    {
        var diffFocused = Focus == FocusPane.Activity;
        var lines = new List<string>
        {
            Fit(PaneTitle("FILE DIFF", diffFocused), width)
        };

        var fileChange = SelectedFileChange();
        if (fileChange == null)
        {
            lines.Add(Fit("press enter on a changed file to show its diff", width));
            return lines;
        }

        if (fileChange.DiffState == WorkspaceFileDiffState.Unloaded)
        {
            lines.Add(ChangeTargetLineText(fileChange.DisplayPath, fileChange.Additions, fileChange.Deletions, width, false, false));
            lines.Add(Fit("press enter to load diff", width));
            return lines;
        }

        if (fileChange.DiffState == WorkspaceFileDiffState.Loading)
        {
            lines.Add(ChangeTargetLineText(fileChange.DisplayPath, fileChange.Additions, fileChange.Deletions, width, false, false));
            lines.Add(Fit("loading diff...", width));
            return lines;
        }

        if (fileChange.DiffState == WorkspaceFileDiffState.Unavailable)
        {
            lines.Add(Fit("diff unavailable", width));
            return lines;
        }

        if (fileChange.DiffLines == null || fileChange.DiffLines.Count == 0)
        {
            lines.Add(Fit("no diff output", width));
            return lines;
        }

        var visibleRows = Math.Max(0, maxRows - lines.Count);
        var start = Math.Min(DiffScrollOffset, Math.Max(0, fileChange.DiffLines.Count - visibleRows));
        for (int i = start; i < Math.Min(start + visibleRows, fileChange.DiffLines.Count); i++)
        {
            lines.Add(Fit(fileChange.DiffLines[i].Text, width));
        }

        return lines;
    }

    private List<string> ConversationEventsLines(int width, ControlPlaneIssueSnapshot issue, int maxRows)
    {
        var activityFocused = Focus == FocusPane.Activity && !DetailDiffOpen;
        var lines = new List<string>
        {
            Fit(PaneTitle("CONVERSATION EVENTS", activityFocused), width)
        };

        if (issue.RecentEvents.Count == 0)
        {
            lines.Add(Fit("no recent events", width));
            return lines;
        }

        var visibleRows = Math.Max(0, maxRows - lines.Count);
        var start = Math.Min(ConversationScrollOffset, Math.Max(0, issue.RecentEvents.Count - visibleRows));
        for (int i = start; i < Math.Min(start + visibleRows, issue.RecentEvents.Count); i++)
        {
            var evt = issue.RecentEvents[i];
            var line = $"{FormatTimestamp(evt.HappenedAt)} {evt.Kind} {evt.Summary}";
            lines.Add(Fit(line, width));
        }

        return lines;
    }

    private List<string> TimelineLines(int width)
    {
        var title = TimelineMode == TimelineMode.Events ? "RECENT EVENTS" : "METRICS";
        var lines = new List<string>
        {
            Fit(PaneTitle(title, Focus == FocusPane.Activity), width)
        };

        if (TimelineMode == TimelineMode.Metrics)
        {
            if (LatestSnapshot == null)
            {
                lines.Add(Fit("no metrics", width));
            }
            else
            {
                var metrics = LatestSnapshot.Snapshot.Metrics;
                lines.Add(Fit($"running_issues: {metrics.RunningIssues}", width));
                lines.Add(Fit($"retry_queue_depth: {metrics.RetryQueueDepth}", width));
                lines.Add(Fit($"input_tokens: {FormatMetric(metrics.InputTokens)}", width));
                lines.Add(Fit($"output_tokens: {FormatMetric(metrics.OutputTokens)}", width));
                lines.Add(Fit($"cache_read_tokens: {FormatMetric(metrics.CacheReadTokens)}", width));
                lines.Add(Fit($"total_tokens: {FormatMetric(metrics.TotalTokens)}", width));
                lines.Add(Fit($"total_cost_micros: {FormatMetric(metrics.TotalCostMicros)}", width));
            }
            return lines;
        }

        // Events mode
        if (LatestSnapshot == null || LatestSnapshot.Snapshot.RecentEvents.Count == 0)
        {
            lines.Add(Fit("no recent events", width));
            return lines;
        }

        foreach (var evt in LatestSnapshot.Snapshot.RecentEvents.Take(10))
        {
            var line = $"{FormatTimestamp(evt.HappenedAt)} {evt.Kind} {evt.Summary}";
            lines.Add(Fit(line, width));
        }

        return lines;
    }

    private string BranchText(ControlPlaneIssueSnapshot issue)
    {
        if (!WorkspaceStatus.TryGetValue(issue.Identifier, out var entry) || entry.IsLoading)
            return "loading...";

        return entry.Branch ?? "unknown";
    }

    private string PrText(ControlPlaneIssueSnapshot issue)
    {
        if (!WorkspaceStatus.TryGetValue(issue.Identifier, out var entry) || entry.IsLoading)
            return "loading...";

        return entry.PrUrl ?? "none";
    }

    private static string PaneTitle(string title, bool focused)
    {
        return focused ? $"[x] {title}" : $"[ ] {title}";
    }

    private static string ChangeSummaryLineText(WorkspaceChangeSummary summary)
    {
        return $"{summary.FilesChanged} files changed +{summary.Additions} -{summary.Deletions}";
    }

    private static string ChangeTargetLineText(string path, ulong? additions, ulong? deletions, int width, bool selected, bool diffOpen)
    {
        var marker = selected ? (diffOpen ? "▼" : "▶") : " ";
        var additionsText = additions.HasValue ? $"+{FormatMetric(additions.Value)}" : "";
        var deletionsText = deletions.HasValue ? $"-{FormatMetric(deletions.Value)}" : "";
        var stats = string.IsNullOrEmpty(additionsText) && string.IsNullOrEmpty(deletionsText)
            ? ""
            : $" ({additionsText} {deletionsText})";
        var line = $"{marker} {path}{stats}";
        return Fit(line, width);
    }

    private static string FormatMetric(ulong value)
    {
        if (value >= 1_000_000_000_000)
            return $"{value / 1_000_000_000_000.0:F2}T";
        if (value >= 1_000_000_000)
            return $"{value / 1_000_000_000.0:F2}B";
        if (value >= 1_000_000)
            return $"{value / 1_000_000.0:F2}M";
        if (value >= 1_000)
            return $"{value / 1_000.0:F1}k";
        return value.ToString();
    }

    private static string FormatTimestamp(DateTimeOffset timestamp)
    {
        return timestamp.ToString("HH:mm:ss");
    }

    private static (int start, int end) IssueWindow(int totalCount, int selectedIndex, int visibleCount)
    {
        if (totalCount <= visibleCount)
            return (0, totalCount);

        var halfVisible = visibleCount / 2;
        var start = Math.Max(0, selectedIndex - halfVisible);
        var end = Math.Min(totalCount, start + visibleCount);

        if (end - start < visibleCount)
        {
            start = Math.Max(0, end - visibleCount);
        }

        return (start, end);
    }

    private static int VisibleIssueCount(int maxRows)
    {
        return Math.Max(1, maxRows - 1); // -1 for title
    }

    private const int MIN_TIMELINE_LINES = 4;
    private const int MAX_TIMELINE_LINES = 6;
}