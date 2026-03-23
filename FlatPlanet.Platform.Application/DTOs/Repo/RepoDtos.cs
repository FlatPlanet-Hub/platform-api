namespace FlatPlanet.Platform.Application.DTOs.Repo;

// ── Repo Management ──────────────────────────────────────────────────────────

public sealed record CreateRepoRequest(
    string RepoName,
    string? Description,
    bool IsPrivate = true,
    string? Org = null);

public sealed record RepoResponse(
    string RepoFullName,
    string RepoUrl,
    string CloneUrl,
    string DefaultBranch);

// ── File Operations ──────────────────────────────────────────────────────────

public sealed record FileItemDto(
    string Name,
    string Path,
    string Type,
    long? Size);

public sealed record FileContentDto(
    string Type,
    string Name,
    string Path,
    string Content,
    string Sha,
    long Size);

public sealed record DirectoryContentDto(
    string Type,
    string Path,
    IReadOnlyList<FileItemDto> Items);

public sealed record TreeItemDto(
    string Path,
    string Type,
    long? Size);

public sealed record TreeResponse(
    string Sha,
    IReadOnlyList<TreeItemDto> Tree);

public sealed record UpsertFileRequest(
    string Path,
    string Content,
    string Message,
    string Branch,
    string? Sha = null);

public sealed record DeleteFileRequest(
    string Path,
    string Message,
    string Branch,
    string Sha);

// ── Commit Operations ────────────────────────────────────────────────────────

public sealed record CommitFileEntry(
    string Path,
    string Action,           // "create" | "update" | "delete"
    string? Content = null);

public sealed record CreateCommitRequest(
    string Message,
    string Branch,
    IReadOnlyList<CommitFileEntry> Files);

public sealed record CommitResponse(
    string CommitSha,
    string CommitUrl,
    int FilesChanged);

public sealed record CommitSummaryDto(
    string Sha,
    string Message,
    string Author,
    DateTimeOffset Date);

// ── Branch Operations ────────────────────────────────────────────────────────

public sealed record BranchDto(
    string Name,
    bool IsDefault,
    string Sha);

public sealed record CreateBranchRequest(
    string Name,
    string FromBranch);

// ── Pull Request Operations ──────────────────────────────────────────────────

public sealed record CreatePullRequestRequest(
    string Title,
    string? Body,
    string Head,
    string Base);

public sealed record PullRequestDto(
    int Number,
    string Title,
    string State,
    string Head,
    string Base,
    string Url,
    string Author,
    DateTimeOffset CreatedAt);

public sealed record MergePullRequestRequest(
    string MergeMethod = "merge");   // "merge" | "squash" | "rebase"

public sealed record MergeResultDto(
    string CommitSha,
    bool Merged,
    string Message);

// ── Collaborator Operations ──────────────────────────────────────────────────

public sealed record CollaboratorDto(
    string GitHubUsername,
    string AvatarUrl,
    string Permission);

public sealed record InviteCollaboratorRequest(
    string GitHubUsername,
    string Permission);    // "pull" | "push" | "admin"
