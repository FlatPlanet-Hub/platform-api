using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FlatPlanet.Platform.Application.DTOs;
using FlatPlanet.Platform.Application.DTOs.Repo;
using FlatPlanet.Platform.Application.Interfaces;

namespace FlatPlanet.Platform.API.Controllers;

[Route("api/projects/{projectId:guid}/repo")]
[Authorize]
public sealed class RepoController : ApiControllerBase
{
    private readonly IGitHubRepoService _repoService;

    public RepoController(IGitHubRepoService repoService) => _repoService = repoService;

    // ── Repo Management ──────────────────────────────────────────────────────

    [HttpPost]
    public async Task<ActionResult<ApiResponse<RepoResponse>>> CreateRepo(
        Guid projectId, [FromBody] CreateRepoRequest request)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        var result = await _repoService.CreateRepoAsync(userId.Value, projectId, request);
        return Ok(ApiResponse<RepoResponse>.Ok(result));
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<RepoResponse>>> GetRepo(Guid projectId)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        var result = await _repoService.GetRepoAsync(userId.Value, projectId);
        return Ok(ApiResponse<RepoResponse>.Ok(result));
    }

    [HttpDelete]
    public async Task<ActionResult<ApiResponse<object?>>> DeleteRepo(
        Guid projectId, [FromHeader(Name = "X-Confirm-Delete")] string? confirm)
    {
        if (confirm != "true")
            return BadRequest(ApiResponse<object?>.Fail("Include header 'X-Confirm-Delete: true' to confirm deletion."));

        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        await _repoService.DeleteRepoAsync(userId.Value, projectId);
        return Ok(ApiResponse<object?>.Ok(null));
    }

    // ── File Operations ──────────────────────────────────────────────────────

    [HttpGet("files")]
    public async Task<ActionResult<ApiResponse<object>>> GetFiles(
        Guid projectId, [FromQuery] string? path, [FromQuery] string? ref_)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        var result = await _repoService.GetFilesAsync(userId.Value, projectId, path, ref_);
        return Ok(ApiResponse<object>.Ok(result));
    }

    [HttpGet("tree")]
    public async Task<ActionResult<ApiResponse<TreeResponse>>> GetTree(
        Guid projectId, [FromQuery] string? ref_)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        var result = await _repoService.GetTreeAsync(userId.Value, projectId, ref_);
        return Ok(ApiResponse<TreeResponse>.Ok(result));
    }

    [HttpPut("files")]
    public async Task<ActionResult<ApiResponse<FileContentDto>>> UpsertFile(
        Guid projectId, [FromBody] UpsertFileRequest request)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        var result = await _repoService.UpsertFileAsync(userId.Value, projectId, request);
        return Ok(ApiResponse<FileContentDto>.Ok(result));
    }

    [HttpDelete("files")]
    public async Task<ActionResult<ApiResponse<object?>>> DeleteFile(
        Guid projectId, [FromBody] DeleteFileRequest request)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        await _repoService.DeleteFileAsync(userId.Value, projectId, request);
        return Ok(ApiResponse<object?>.Ok(null));
    }

    // ── Commit Operations ────────────────────────────────────────────────────

    [HttpPost("commits")]
    public async Task<ActionResult<ApiResponse<CommitResponse>>> CreateCommit(
        Guid projectId, [FromBody] CreateCommitRequest request)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        var result = await _repoService.CreateMultiFileCommitAsync(userId.Value, projectId, request);
        return Ok(ApiResponse<CommitResponse>.Ok(result));
    }

    [HttpGet("commits")]
    public async Task<ActionResult<ApiResponse<IEnumerable<CommitSummaryDto>>>> ListCommits(
        Guid projectId, [FromQuery] string? branch,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        var result = await _repoService.ListCommitsAsync(userId.Value, projectId, branch, page, pageSize);
        return Ok(ApiResponse<IEnumerable<CommitSummaryDto>>.Ok(result));
    }

    // ── Branch Operations ────────────────────────────────────────────────────

    [HttpGet("branches")]
    public async Task<ActionResult<ApiResponse<IEnumerable<BranchDto>>>> ListBranches(Guid projectId)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        var result = await _repoService.ListBranchesAsync(userId.Value, projectId);
        return Ok(ApiResponse<IEnumerable<BranchDto>>.Ok(result));
    }

    [HttpPost("branches")]
    public async Task<ActionResult<ApiResponse<BranchDto>>> CreateBranch(
        Guid projectId, [FromBody] CreateBranchRequest request)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        var result = await _repoService.CreateBranchAsync(userId.Value, projectId, request);
        return Ok(ApiResponse<BranchDto>.Ok(result));
    }

    [HttpDelete("branches/{branchName}")]
    public async Task<ActionResult<ApiResponse<object?>>> DeleteBranch(Guid projectId, string branchName)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        await _repoService.DeleteBranchAsync(userId.Value, projectId, branchName);
        return Ok(ApiResponse<object?>.Ok(null));
    }

    // ── Pull Request Operations ──────────────────────────────────────────────

    [HttpPost("pulls")]
    public async Task<ActionResult<ApiResponse<PullRequestDto>>> CreatePullRequest(
        Guid projectId, [FromBody] CreatePullRequestRequest request)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        var result = await _repoService.CreatePullRequestAsync(userId.Value, projectId, request);
        return Ok(ApiResponse<PullRequestDto>.Ok(result));
    }

    [HttpGet("pulls")]
    public async Task<ActionResult<ApiResponse<IEnumerable<PullRequestDto>>>> ListPullRequests(
        Guid projectId, [FromQuery] string? state)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        var result = await _repoService.ListPullRequestsAsync(userId.Value, projectId, state);
        return Ok(ApiResponse<IEnumerable<PullRequestDto>>.Ok(result));
    }

    [HttpGet("pulls/{prNumber:int}")]
    public async Task<ActionResult<ApiResponse<PullRequestDto>>> GetPullRequest(
        Guid projectId, int prNumber)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        var result = await _repoService.GetPullRequestAsync(userId.Value, projectId, prNumber);
        return Ok(ApiResponse<PullRequestDto>.Ok(result));
    }

    [HttpPut("pulls/{prNumber:int}/merge")]
    public async Task<ActionResult<ApiResponse<MergeResultDto>>> MergePullRequest(
        Guid projectId, int prNumber, [FromBody] MergePullRequestRequest request)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        var result = await _repoService.MergePullRequestAsync(userId.Value, projectId, prNumber, request);
        return Ok(ApiResponse<MergeResultDto>.Ok(result));
    }

    // ── Collaborator Operations ──────────────────────────────────────────────

    [HttpGet("collaborators")]
    public async Task<ActionResult<ApiResponse<IEnumerable<CollaboratorDto>>>> ListCollaborators(Guid projectId)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        var result = await _repoService.ListCollaboratorsAsync(userId.Value, projectId);
        return Ok(ApiResponse<IEnumerable<CollaboratorDto>>.Ok(result));
    }

    [HttpPost("collaborators")]
    public async Task<ActionResult<ApiResponse<object?>>> InviteCollaborator(
        Guid projectId, [FromBody] InviteCollaboratorRequest request)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        await _repoService.InviteCollaboratorAsync(userId.Value, projectId, request);
        return Ok(ApiResponse<object?>.Ok(null));
    }

    [HttpDelete("collaborators/{githubUsername}")]
    public async Task<ActionResult<ApiResponse<object?>>> RemoveCollaborator(
        Guid projectId, string githubUsername)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        await _repoService.RemoveCollaboratorAsync(userId.Value, projectId, githubUsername);
        return Ok(ApiResponse<object?>.Ok(null));
    }
}
