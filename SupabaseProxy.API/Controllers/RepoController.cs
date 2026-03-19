using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SupabaseProxy.Application.DTOs;
using SupabaseProxy.Application.DTOs.Repo;
using SupabaseProxy.Application.Interfaces;

namespace SupabaseProxy.API.Controllers;

[ApiController]
[Route("api/projects/{projectId:guid}/repo")]
[Authorize]
public sealed class RepoController : ControllerBase
{
    private readonly IGitHubRepoService _repoService;

    public RepoController(IGitHubRepoService repoService) => _repoService = repoService;

    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    // ── Repo Management ──────────────────────────────────────────────────────

    [HttpPost]
    public async Task<ActionResult<ApiResponse<RepoResponse>>> CreateRepo(
        Guid projectId, [FromBody] CreateRepoRequest request)
    {
        try
        {
            var result = await _repoService.CreateRepoAsync(UserId, projectId, request);
            return Ok(ApiResponse<RepoResponse>.Ok(result));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<RepoResponse>.Fail(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<RepoResponse>.Fail(ex.Message));
        }
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<RepoResponse>>> GetRepo(Guid projectId)
    {
        try
        {
            var result = await _repoService.GetRepoAsync(UserId, projectId);
            return Ok(ApiResponse<RepoResponse>.Ok(result));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<RepoResponse>.Fail(ex.Message));
        }
    }

    [HttpDelete]
    public async Task<ActionResult<ApiResponse<object?>>> DeleteRepo(
        Guid projectId, [FromHeader(Name = "X-Confirm-Delete")] string? confirm)
    {
        if (confirm != "true")
            return BadRequest(ApiResponse<object?>.Fail("Include header 'X-Confirm-Delete: true' to confirm deletion."));

        try
        {
            await _repoService.DeleteRepoAsync(UserId, projectId);
            return Ok(ApiResponse<object?>.Ok(null));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object?>.Fail(ex.Message));
        }
    }

    // ── File Operations ──────────────────────────────────────────────────────

    [HttpGet("files")]
    public async Task<ActionResult<ApiResponse<object>>> GetFiles(
        Guid projectId, [FromQuery] string? path, [FromQuery] string? ref_)
    {
        try
        {
            var result = await _repoService.GetFilesAsync(UserId, projectId, path, ref_);
            return Ok(ApiResponse<object>.Ok(result));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.Fail(ex.Message));
        }
    }

    [HttpGet("tree")]
    public async Task<ActionResult<ApiResponse<TreeResponse>>> GetTree(
        Guid projectId, [FromQuery] string? ref_)
    {
        try
        {
            var result = await _repoService.GetTreeAsync(UserId, projectId, ref_);
            return Ok(ApiResponse<TreeResponse>.Ok(result));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<TreeResponse>.Fail(ex.Message));
        }
    }

    [HttpPut("files")]
    public async Task<ActionResult<ApiResponse<FileContentDto>>> UpsertFile(
        Guid projectId, [FromBody] UpsertFileRequest request)
    {
        try
        {
            var result = await _repoService.UpsertFileAsync(UserId, projectId, request);
            return Ok(ApiResponse<FileContentDto>.Ok(result));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<FileContentDto>.Fail(ex.Message));
        }
    }

    [HttpDelete("files")]
    public async Task<ActionResult<ApiResponse<object?>>> DeleteFile(
        Guid projectId, [FromBody] DeleteFileRequest request)
    {
        try
        {
            await _repoService.DeleteFileAsync(UserId, projectId, request);
            return Ok(ApiResponse<object?>.Ok(null));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object?>.Fail(ex.Message));
        }
    }

    // ── Commit Operations ────────────────────────────────────────────────────

    [HttpPost("commits")]
    public async Task<ActionResult<ApiResponse<CommitResponse>>> CreateCommit(
        Guid projectId, [FromBody] CreateCommitRequest request)
    {
        try
        {
            var result = await _repoService.CreateMultiFileCommitAsync(UserId, projectId, request);
            return Ok(ApiResponse<CommitResponse>.Ok(result));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<CommitResponse>.Fail(ex.Message));
        }
    }

    [HttpGet("commits")]
    public async Task<ActionResult<ApiResponse<IEnumerable<CommitSummaryDto>>>> ListCommits(
        Guid projectId, [FromQuery] string? branch,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var result = await _repoService.ListCommitsAsync(UserId, projectId, branch, page, pageSize);
        return Ok(ApiResponse<IEnumerable<CommitSummaryDto>>.Ok(result));
    }

    // ── Branch Operations ────────────────────────────────────────────────────

    [HttpGet("branches")]
    public async Task<ActionResult<ApiResponse<IEnumerable<BranchDto>>>> ListBranches(Guid projectId)
    {
        var result = await _repoService.ListBranchesAsync(UserId, projectId);
        return Ok(ApiResponse<IEnumerable<BranchDto>>.Ok(result));
    }

    [HttpPost("branches")]
    public async Task<ActionResult<ApiResponse<BranchDto>>> CreateBranch(
        Guid projectId, [FromBody] CreateBranchRequest request)
    {
        try
        {
            var result = await _repoService.CreateBranchAsync(UserId, projectId, request);
            return Ok(ApiResponse<BranchDto>.Ok(result));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<BranchDto>.Fail(ex.Message));
        }
    }

    [HttpDelete("branches/{branchName}")]
    public async Task<ActionResult<ApiResponse<object?>>> DeleteBranch(Guid projectId, string branchName)
    {
        try
        {
            await _repoService.DeleteBranchAsync(UserId, projectId, branchName);
            return Ok(ApiResponse<object?>.Ok(null));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object?>.Fail(ex.Message));
        }
    }

    // ── Pull Request Operations ──────────────────────────────────────────────

    [HttpPost("pulls")]
    public async Task<ActionResult<ApiResponse<PullRequestDto>>> CreatePullRequest(
        Guid projectId, [FromBody] CreatePullRequestRequest request)
    {
        try
        {
            var result = await _repoService.CreatePullRequestAsync(UserId, projectId, request);
            return Ok(ApiResponse<PullRequestDto>.Ok(result));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<PullRequestDto>.Fail(ex.Message));
        }
    }

    [HttpGet("pulls")]
    public async Task<ActionResult<ApiResponse<IEnumerable<PullRequestDto>>>> ListPullRequests(
        Guid projectId, [FromQuery] string? state)
    {
        var result = await _repoService.ListPullRequestsAsync(UserId, projectId, state);
        return Ok(ApiResponse<IEnumerable<PullRequestDto>>.Ok(result));
    }

    [HttpGet("pulls/{prNumber:int}")]
    public async Task<ActionResult<ApiResponse<PullRequestDto>>> GetPullRequest(
        Guid projectId, int prNumber)
    {
        try
        {
            var result = await _repoService.GetPullRequestAsync(UserId, projectId, prNumber);
            return Ok(ApiResponse<PullRequestDto>.Ok(result));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<PullRequestDto>.Fail(ex.Message));
        }
    }

    [HttpPut("pulls/{prNumber:int}/merge")]
    public async Task<ActionResult<ApiResponse<MergeResultDto>>> MergePullRequest(
        Guid projectId, int prNumber, [FromBody] MergePullRequestRequest request)
    {
        try
        {
            var result = await _repoService.MergePullRequestAsync(UserId, projectId, prNumber, request);
            return Ok(ApiResponse<MergeResultDto>.Ok(result));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<MergeResultDto>.Fail(ex.Message));
        }
    }

    // ── Collaborator Operations ──────────────────────────────────────────────

    [HttpGet("collaborators")]
    public async Task<ActionResult<ApiResponse<IEnumerable<CollaboratorDto>>>> ListCollaborators(Guid projectId)
    {
        var result = await _repoService.ListCollaboratorsAsync(UserId, projectId);
        return Ok(ApiResponse<IEnumerable<CollaboratorDto>>.Ok(result));
    }

    [HttpPost("collaborators")]
    public async Task<ActionResult<ApiResponse<object?>>> InviteCollaborator(
        Guid projectId, [FromBody] InviteCollaboratorRequest request)
    {
        try
        {
            await _repoService.InviteCollaboratorAsync(UserId, projectId, request);
            return Ok(ApiResponse<object?>.Ok(null));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object?>.Fail(ex.Message));
        }
    }

    [HttpDelete("collaborators/{githubUsername}")]
    public async Task<ActionResult<ApiResponse<object?>>> RemoveCollaborator(
        Guid projectId, string githubUsername)
    {
        try
        {
            await _repoService.RemoveCollaboratorAsync(UserId, projectId, githubUsername);
            return Ok(ApiResponse<object?>.Ok(null));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object?>.Fail(ex.Message));
        }
    }
}
