using FlatPlanet.Platform.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlatPlanet.Platform.API.Controllers;

[Authorize]
[ApiController]
[Route("api/v1/dataverse")]
public class DataverseController : ApiControllerBase
{
    private readonly IDataverseService _dataverseService;

    public DataverseController(IDataverseService dataverseService)
    {
        _dataverseService = dataverseService;
    }

    [HttpGet("employees")]
    public async Task<IActionResult> GetEmployees()
    {
        var result = await _dataverseService.GetEmployeesAsync();
        return OkData(result);
    }

    [HttpGet("accounts")]
    public async Task<IActionResult> GetAccounts()
    {
        var result = await _dataverseService.GetAccountsAsync();
        return OkData(result);
    }
}
