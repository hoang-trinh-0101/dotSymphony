namespace OpenSymphony.Planning.GraphValidate;

using OpenSymphony.Planning.Generator;

public sealed class DependencyGraphBuilder
{
    public static DependencyGraph Build(PlanArtifacts artifacts)
    {
        var nodes = new List<GraphNode>();
        var edges = new List<GraphEdge>();

        foreach (var milestone in artifacts.Milestones)
        {
            CollectMilestoneNodes(milestone, nodes);
            CollectMilestoneEdges(milestone, edges);
        }

        nodes.Sort((a, b) =>
        {
            var c = string.Compare(a.Milestone, b.Milestone, StringComparison.Ordinal);
            if (c != 0) return c;
            c = KindRank(a.Kind).CompareTo(KindRank(b.Kind));
            if (c != 0) return c;
            return a.Id.CompareTo(b.Id);
        });

        edges.Sort((a, b) =>
        {
            var c = string.Compare(a.Milestone, b.Milestone, StringComparison.Ordinal);
            if (c != 0) return c;
            c = a.From.CompareTo(b.From);
            if (c != 0) return c;
            c = RelationRank(a.Relation).CompareTo(RelationRank(b.Relation));
            if (c != 0) return c;
            return a.To.CompareTo(b.To);
        });

        var parallelizableWaves = ParallelizableWavesDeterministic(artifacts);

        return new DependencyGraph(artifacts.PlanningWave, DateTime.UtcNow, nodes, edges, parallelizableWaves);
    }

    private static int KindRank(GraphNodeKind kind) => kind switch
    {
        GraphNodeKind.Milestone => 0,
        GraphNodeKind.Issue => 1,
        GraphNodeKind.SubIssue => 2,
        _ => 3,
    };

    private static int RelationRank(GraphEdgeReason relation) => relation switch
    {
        GraphEdgeReason.ParentOf => 0,
        GraphEdgeReason.BlockedBy => 1,
        GraphEdgeReason.BlocksInvariant => 2,
        GraphEdgeReason.MissingInverse => 3,
        GraphEdgeReason.UnknownTarget => 4,
        _ => 5,
    };

    private static void CollectMilestoneNodes(PlannedMilestone milestone, List<GraphNode> nodes)
    {
        nodes.Add(new GraphNode(milestone.Id, GraphNodeKind.Milestone, milestone.Name, milestone.Name,
            milestone.AcceptanceCriteria.Count, milestone.VerificationSteps.Count, null));
        foreach (var issue in milestone.Issues)
        {
            nodes.Add(new GraphNode(issue.Id, GraphNodeKind.Issue, issue.Title, milestone.Name,
                issue.AcceptanceCriteria.Count, issue.VerificationSteps.Count, issue.TaskFile));
            foreach (var sub in issue.SubIssues)
            {
                nodes.Add(new GraphNode(sub.Id, GraphNodeKind.SubIssue, sub.Title, milestone.Name,
                    sub.AcceptanceCriteria.Count, sub.VerificationSteps.Count, sub.TaskFile));
            }
        }
    }

    private static void CollectMilestoneEdges(PlannedMilestone milestone, List<GraphEdge> edges)
    {
        var declaredIds = CollectAllTaskIds(milestone);
        var sourceFor = BuildTaskFileLookup(milestone);
        foreach (var issue in milestone.Issues)
        {
            foreach (var sub in issue.SubIssues)
            {
                PushParentEdge(sub.Id, issue.Id, milestone, edges);
                PushBlockerEdges(new SubIssueBlockingTask(sub), milestone, declaredIds, sourceFor, edges);
            }
            PushBlockerEdges(new IssueBlockingTask(issue), milestone, declaredIds, sourceFor, edges);
        }
    }

    private static void PushParentEdge(TaskId child, TaskId parent, PlannedMilestone milestone, List<GraphEdge> edges)
    {
        edges.Add(new GraphEdge(parent, child, GraphEdgeReason.ParentOf, milestone.Name, null));
    }

    private static void PushBlockerEdges(IBlockingTask task, PlannedMilestone milestone, SortedSet<TaskId> declaredIds, SortedDictionary<TaskId, string?> sourceFor, List<GraphEdge> edges)
    {
        foreach (var blocker in task.GetBlockedBy())
        {
            var reason = !declaredIds.Contains(blocker) ? GraphEdgeReason.UnknownTarget : GraphEdgeReason.BlockedBy;
            edges.Add(new GraphEdge(blocker, task.Id, reason, milestone.Name,
                sourceFor.TryGetValue(blocker, out var f) ? f : null));
        }

        var blocksPairs = task.GetBlocks().Select(blocked => (task.Id, blocked)).ToList();
        blocksPairs.Sort();
        foreach (var (source, target) in blocksPairs)
        {
            var reason = !declaredIds.Contains(target) ? GraphEdgeReason.UnknownTarget : GraphEdgeReason.BlocksInvariant;
            edges.Add(new GraphEdge(source, target, reason, milestone.Name,
                sourceFor.TryGetValue(source, out var f) ? f : null));
        }
    }

    private static SortedSet<TaskId> CollectAllTaskIds(PlannedMilestone milestone)
    {
        var ids = new SortedSet<TaskId>();
        ids.Add(milestone.Id);
        foreach (var issue in milestone.Issues)
        {
            ids.Add(issue.Id);
            foreach (var sub in issue.SubIssues)
                ids.Add(sub.Id);
        }
        return ids;
    }

    private static SortedDictionary<TaskId, string?> BuildTaskFileLookup(PlannedMilestone milestone)
    {
        var lookup = new SortedDictionary<TaskId, string?>();
        foreach (var issue in milestone.Issues)
        {
            lookup[issue.Id] = issue.TaskFile;
            foreach (var sub in issue.SubIssues)
                lookup[sub.Id] = sub.TaskFile;
        }
        return lookup;
    }

    private static List<List<TaskId>> ParallelizableWavesDeterministic(PlanArtifacts artifacts)
    {
        var waves = BlockingTaskHelpers.CreationOrderWaves(artifacts);
        foreach (var wave in waves)
            wave.Sort();
        return waves;
    }
}
