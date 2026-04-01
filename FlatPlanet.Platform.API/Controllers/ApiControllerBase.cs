using Microsoft.AspNetCore.Mvc;
using FlatPlanet.Platform.Application.DTOs;

namespace FlatPlanet.Platform.API.Controllers;

[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    protected Guid? GetUserId()
    {
        var sub = User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value
                  ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out var id) ? id : null;
    }

    protected OkObjectResult OkData<T>(T data) => Ok(ApiResponse<T>.Ok(data));
}
