using System.ComponentModel.DataAnnotations;
using FlatPlanet.Platform.Application.DTOs.Storage;
using FlatPlanet.Platform.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlatPlanet.Platform.API.Controllers;

[Authorize]
[ApiController]
[Route("api/v1/storage")]
public class StorageController : ApiControllerBase
{
    private readonly IFileStorageService _storageService;

    public StorageController(IFileStorageService storageService)
    {
        _storageService = storageService;
    }

    [HttpPost("upload")]
    [RequestSizeLimit(52_428_800)] // 50MB
    public async Task<IActionResult> Upload(
        IFormFile file,
        [FromForm] string businessCode,
        [FromForm] string category = "general",
        [FromForm] string? tags = null)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        // Scope files to the calling app — extracted from the JWT app_id claim.
        // API tokens (project-scoped JWTs) carry app_id; SP user JWTs do not.
        Guid? appId = Guid.TryParse(User.FindFirst("app_id")?.Value, out var aid) ? aid : null;

        if (string.IsNullOrWhiteSpace(category)) category = "general";

        var tagArray = tags?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? [];
        var request = new UploadFileRequest(businessCode, category, tagArray, appId);

        await using var stream = file.OpenReadStream();
        var result = await _storageService.UploadAsync(
            stream, file.FileName, file.ContentType, file.Length, request, userId.Value);

        return Ok(result);
    }

    [HttpGet("files")]
    [ProducesResponseType(typeof(IEnumerable<FileDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List(
        [FromQuery][Required] string businessCode,
        [FromQuery] string? category = null,
        [FromQuery] string? tags = null)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        if (string.IsNullOrWhiteSpace(businessCode))
            return BadRequest(new { error = "businessCode is required." });

        // Scope listing to the calling app — same app_id extraction as upload.
        Guid? appId = Guid.TryParse(User.FindFirst("app_id")?.Value, out var aid) ? aid : null;

        var tagArray = tags?.Split(',', StringSplitOptions.RemoveEmptyEntries);
        var result = await _storageService.ListAsync(businessCode, category, tagArray, appId, userId);
        return Ok(result);
    }

    [HttpGet("files/{fileId:guid}/url")]
    public async Task<IActionResult> GetUrl(Guid fileId)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        Guid? appId = Guid.TryParse(User.FindFirst("app_id")?.Value, out var aid) ? aid : null;

        var result = await _storageService.GetSasUrlAsync(fileId, userId.Value, appId);
        return Ok(result);
    }

    [HttpDelete("files/{fileId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid fileId)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        Guid? appId = Guid.TryParse(User.FindFirst("app_id")?.Value, out var aid) ? aid : null;

        await _storageService.DeleteAsync(fileId, userId.Value, appId);
        return Ok(new { success = true, message = "File deleted." });
    }
}
