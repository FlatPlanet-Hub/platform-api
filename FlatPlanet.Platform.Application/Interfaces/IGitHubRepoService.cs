using FlatPlanet.Platform.Application.DTOs.Repo;

namespace FlatPlanet.Platform.Application.Interfaces;

public interface IGitHubRepoService
{
    // ── Repo management ──────────────────────────────────────────────────────
    Task<RepoResponse> CreateRepoAsync(Guid userId, Guid projectId, CreateRepoRequest request);
    Task<RepoResponse> GetRepoAsync(Guid userId, Guid projectId);
    Task DeleteRepoAsync(Guid userId, Guid projectId);

    // ── File operations ──────────────────────────────────────────────────────
    Task<object> GetFilesAsync(Guid userId, Guid projectId, string? path, string? branch);
    Task<TreeResponse> GetTreeAsync(Guid userId, Guid projectId, string? branch);
    Task<FileContentDto> UpsertFileAsync(Guid userId, Guid projectId, UpsertFileRequest request);
    Task DeleteFileAsync(Guid userId, Guid projectId, DeleteFileRequest request);

    // ── Commit operations ────────────────────────────────────────────────────
    Task<CommitResponse> CreateMultiFileCommitAsync(Guid userId, Guid projectId, CreateCommitRequest request);
    Task<IEnumerable<CommitSummaryDto>> ListCommitsAsync(Guid userId, Guid projectId, string? branch, int page, int pageSize);

    // ── Branch operations ────────────────────────────────────────────────────
    Task<IEnumerable<BranchDto>> ListBranchesAsync(Guid userId, Guid projectId);
    Task<BranchDto> CreateBranchAsync(Guid userId, Guid projectId, CreateBranchRequest request);
    Task DeleteBranchAsync(Guid userId, Guid projectId, string branchName);

    // ── Pull request operations ──────────────────────────────────────────────
    Task<PullRequestDto> CreatePullRequestAsync(Guid userId, Guid projectId, CreatePullRequestRequest request);
    Task<IEnumerable<PullRequestDto>> ListPullRequestsAsync(Guid userId, Guid projectId, string? state);
    Task<PullRequestDto> GetPullRequestAsync(Guid userId, Guid projectId, int prNumber);
    Task<MergeResultDto> MergePullRequestAsync(Guid userId, Guid projectId, int prNumber, MergePullRequestRequest request);

    // ── Collaborator operations ──────────────────────────────────────────────
    Task<IEnumerable<CollaboratorDto>> ListCollaboratorsAsync(Guid userId, Guid projectId);
    Task InviteCollaboratorAsync(Guid userId, Guid projectId, InviteCollaboratorRequest request);
    Task RemoveCollaboratorAsync(Guid userId, Guid projectId, string githubUsername);

    // ── Internal ─────────────────────────────────────────────────────────────
    Task SyncDataDictionaryAsync(Guid userId, Guid projectId, string schema);
}
