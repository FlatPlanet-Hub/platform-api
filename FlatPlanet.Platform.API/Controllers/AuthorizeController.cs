using Microsoft.AspNetCore.Mvc;
using FlatPlanet.Platform.Application.DTOs;
using FlatPlanet.Platform.Application.DTOs.Iam;
using FlatPlanet.Platform.Application.Interfaces;

namespace FlatPlanet.Platform.API.Controllers;

[Route("api/authorize")]
public sealed class AuthorizeController(IIamAuthorizationService authorizationService) : ApiControllerBase
{
    /// <summary>
    /// Check if a user can access a resource in an app.
    /// Called by apps at runtime before granting access.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<ApiResponse<AuthorizeResponse>>> Authorize([FromBody] AuthorizeRequest request)
    {
        var result = await authorizationService.AuthorizeAsync(request);
        return Ok(ApiResponse<AuthorizeResponse>.Ok(result));
    }
}
