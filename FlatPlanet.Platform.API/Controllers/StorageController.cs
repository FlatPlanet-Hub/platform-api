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

        var tagArray = tags?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? [];
        var request = new UploadFileRequest(businessCode, category, tagArray);

        await using var stream = file.OpenReadStream();
        var result = await _storageService.UploadAsync(
            stream, file.FileName, file.ContentType, request, userId.Value);

        return Ok(result);
    }

    [HttpGet("files")]
    public async Task<IActionResult> List(
        [FromQuery] string businessCode,
        [FromQuery] string? category = null,
        [FromQuery] string? tags = null)
    {
        var tagArray = tags?.Split(',', StringSplitOptions.RemoveEmptyEntries);
        var result = await _storageService.ListAsync(businessCode, category, tagArray);
        return Ok(result);
    }

    [HttpGet("files/{fileId:guid}/url")]
    public async Task<IActionResult> GetUrl(Guid fileId)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        var result = await _storageService.GetSasUrlAsync(fileId, userId.Value);
        return Ok(result);
    }

    [HttpDelete("files/{fileId:guid}")]
    public async Task<IActionResult> Delete(Guid fileId)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        await _storageService.DeleteAsync(fileId, userId.Value);
        return NoContent();
    }
}
