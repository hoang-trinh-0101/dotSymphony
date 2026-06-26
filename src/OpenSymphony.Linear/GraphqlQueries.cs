namespace OpenSymphony.Linear;

// ht: Port of older/crates/opensymphony-linear/src/graphql.rs query constants.
//   Query strings are verbatim from the Rust source.

internal static class GraphqlQueries
{
    public const string IssuesByStateQuery = """
query IssuesByState($projectSlug: String!, $stateNames: [String!], $includeArchived: Boolean!, $first: Int!, $after: String, $relationFirst: Int!, $labelFirst: Int!) {
  issues(
    filter: {
      project: { slugId: { eq: $projectSlug } }
      state: { name: { in: $stateNames } }
    }
    includeArchived: $includeArchived
    first: $first
    after: $after
  ) {
    nodes {
      id
      identifier
      url
      title
      description
      priority
      createdAt
      updatedAt
      state {
        id
        name
        type
      }
      parent {
        id
        identifier
        url
        title
        state {
          name
        }
      }
      projectMilestone {
        id
        name
      }
      children(includeArchived: true, first: 100) {
        nodes {
          id
          identifier
          url
          title
          state {
            name
          }
        }
      }
      labels(first: $labelFirst) {
        nodes {
          name
        }
        pageInfo {
          hasNextPage
          endCursor
        }
      }
      inverseRelations(first: $relationFirst) {
        nodes {
          type
          issue {
            id
            identifier
            title
            state {
              id
              name
              type
            }
          }
        }
        pageInfo {
          hasNextPage
          endCursor
        }
      }
    }
    pageInfo {
      hasNextPage
      endCursor
    }
  }
}
""";

    public const string ProjectIssuesQuery = """
query ProjectIssues($projectSlug: String!, $includeArchived: Boolean!, $first: Int!, $after: String, $relationFirst: Int!, $labelFirst: Int!) {
  issues(
    filter: {
      project: { slugId: { eq: $projectSlug } }
    }
    includeArchived: $includeArchived
    first: $first
    after: $after
  ) {
    nodes {
      id
      identifier
      url
      title
      description
      priority
      createdAt
      updatedAt
      state {
        id
        name
        type
      }
      parent {
        id
        identifier
        url
        title
        state {
          name
        }
      }
      projectMilestone {
        id
        name
      }
      children(includeArchived: true, first: 100) {
        nodes {
          id
          identifier
          url
          title
          state {
            name
          }
        }
      }
      labels(first: $labelFirst) {
        nodes {
          name
        }
        pageInfo {
          hasNextPage
          endCursor
        }
      }
      inverseRelations(first: $relationFirst) {
        nodes {
          type
          issue {
            id
            identifier
            title
            state {
              id
              name
              type
            }
          }
        }
        pageInfo {
          hasNextPage
          endCursor
        }
      }
    }
    pageInfo {
      hasNextPage
      endCursor
    }
  }
}
""";

    public const string IssueLabelsQuery = """
query IssueLabelsPage($issueId: String!, $first: Int!, $after: String) {
  issue(id: $issueId) {
    id
    labels(first: $first, after: $after) {
      nodes {
        name
      }
      pageInfo {
        hasNextPage
        endCursor
      }
    }
  }
}
""";

    public const string IssueInverseRelationsQuery = """
query IssueInverseRelationsPage($issueId: String!, $first: Int!, $after: String) {
  issue(id: $issueId) {
    id
    inverseRelations(first: $first, after: $after) {
      nodes {
        type
        issue {
          id
          identifier
          title
          state {
            id
            name
            type
          }
        }
      }
      pageInfo {
        hasNextPage
        endCursor
      }
    }
  }
}
""";

    public const string IssueStatesByIdsQuery = """
query IssueStatesByIds($projectSlug: String!, $issueIds: [ID!], $first: Int!, $after: String) {
  issues(
    filter: {
      id: { in: $issueIds }
      project: { slugId: { eq: $projectSlug } }
    }
    includeArchived: true
    first: $first
    after: $after
  ) {
    nodes {
      id
      identifier
      updatedAt
      state {
        id
        name
        type
      }
    }
    pageInfo {
      hasNextPage
      endCursor
    }
  }
}
""";

    public const string IssueByIdentifierQuery = """
query IssueByIdentifier($identifier: String!, $relationFirst: Int!, $labelFirst: Int!) {
  issue(id: $identifier) {
    id
    identifier
    url
    title
    description
    priority
    createdAt
    updatedAt
    state {
      id
      name
      type
    }
    parent {
      id
      identifier
      url
      title
      state {
        name
      }
    }
    projectMilestone {
      id
      name
    }
    children(includeArchived: true, first: 100) {
      nodes {
        id
        identifier
        url
        title
        state {
          name
        }
      }
    }
    labels(first: $labelFirst) {
      nodes {
        name
      }
      pageInfo {
        hasNextPage
        endCursor
      }
    }
    inverseRelations(first: $relationFirst) {
      nodes {
        type
        issue {
          id
          identifier
          title
          state {
            id
            name
            type
          }
        }
      }
      pageInfo {
        hasNextPage
        endCursor
      }
    }
  }
}
""";

    public const string IssueCommentsQuery = """
query IssueCommentsPage($issueId: String!, $first: Int!, $after: String) {
  issue(id: $issueId) {
    id
    comments(first: $first, after: $after) {
      nodes {
        id
        body
        updatedAt
        resolvedAt
      }
      pageInfo {
        hasNextPage
        endCursor
      }
    }
  }
}
""";

    public const string IssueArchiveMutation = """
mutation IssueArchive($id: String!, $trash: Boolean) {
  issueArchive(id: $id, trash: $trash) {
    success
  }
}
""";

    public const string ProjectMilestoneCreateMutation = """
mutation ProjectMilestoneCreate($input: ProjectMilestoneCreateInput!) {
  projectMilestoneCreate(input: $input) {
    success
    projectMilestone {
      id
      name
      description
      targetDate
      sortOrder
      project {
        id
        name
        slugId
      }
    }
  }
}
""";

    public const string ProjectMilestoneUpdateMutation = """
mutation ProjectMilestoneUpdate($id: String!, $input: ProjectMilestoneUpdateInput!) {
  projectMilestoneUpdate(id: $id, input: $input) {
    success
    projectMilestone {
      id
      name
      description
      targetDate
      sortOrder
      project {
        id
        name
        slugId
      }
    }
  }
}
""";

    public const string IssueCreateMutation = """
mutation IssueCreate($input: IssueCreateInput!) {
  issueCreate(input: $input) {
    success
    issue {
      id
      identifier
      url
      title
      description
      priority
      estimate
      createdAt
      updatedAt
      state {
        id
        name
        type
      }
      project {
        id
        name
        slugId
      }
      projectMilestone {
        id
        name
      }
      parent {
        id
        identifier
      }
      assignee {
        id
        name
        email
      }
      labels(first: 25) {
        nodes {
          id
          name
        }
      }
    }
  }
}
""";

    public const string IssueUpdateMutation = """
mutation IssueUpdate($id: String!, $input: IssueUpdateInput!) {
  issueUpdate(id: $id, input: $input) {
    success
    issue {
      id
      identifier
      url
      title
      description
      priority
      estimate
      createdAt
      updatedAt
      state {
        id
        name
        type
      }
      project {
        id
        name
        slugId
      }
      projectMilestone {
        id
        name
      }
      parent {
        id
        identifier
      }
      assignee {
        id
        name
        email
      }
      labels(first: 25) {
        nodes {
          id
          name
        }
      }
    }
  }
}
""";

    public const string CommentCreateMutation = """
mutation CommentCreate($input: CommentCreateInput!) {
  commentCreate(input: $input) {
    success
    comment {
      id
      body
      url
      createdAt
      updatedAt
      issue {
        id
        identifier
      }
    }
  }
}
""";

    public const string IssueRelationCreateMutation = """
mutation IssueRelationCreate($input: IssueRelationCreateInput!) {
  issueRelationCreate(input: $input) {
    success
    issueRelation {
      id
      type
      issue {
        id
        identifier
      }
      relatedIssue {
        id
        identifier
      }
    }
  }
}
""";

    public const string ProjectBySlugQuery = """
query ProjectBySlug($slug: String!) {
  projects(filter: { slugId: { eq: $slug } }, first: 1) {
    nodes {
      id
      name
      slugId
      url
      content
    }
  }
}
""";

    public const string ProjectUpdateContentMutation = """
mutation UpdateProjectContent($id: String!, $content: String!) {
  projectUpdate(id: $id, input: { content: $content }) {
    success
    project {
      id
      name
      slugId
      content
      updatedAt
    }
  }
}
""";

    public const string IntrospectTypeQuery = """
query IntrospectType($typeName: String!) {
  __type(name: $typeName) {
    kind
    name
    fields(includeDeprecated: true) {
      name
    }
  }
}
""";
}
