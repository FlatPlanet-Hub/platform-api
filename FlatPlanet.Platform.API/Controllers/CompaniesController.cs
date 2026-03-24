using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FlatPlanet.Platform.Application.DTOs;
using FlatPlanet.Platform.Application.DTOs.Iam;
using FlatPlanet.Platform.Application.Interfaces;

namespace FlatPlanet.Platform.API.Controllers;

[Route("api/companies")]
[Authorize]
public sealed class CompaniesController(ICompanyService companyService) : ApiControllerBase
{
    [HttpPost]
    public async Task<ActionResult<ApiResponse<CompanyDto>>> Create([FromBody] CreateCompanyRequest request)
    {
        var result = await companyService.CreateAsync(request);
        return Ok(ApiResponse<CompanyDto>.Ok(result));
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<IEnumerable<CompanyDto>>>> List()
    {
        var result = await companyService.ListAsync();
        return Ok(ApiResponse<IEnumerable<CompanyDto>>.Ok(result));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<CompanyDto>>> GetById(Guid id)
    {
        var result = await companyService.GetByIdAsync(id);
        if (result is null) return NotFound(ApiResponse<object>.Fail("Company not found."));
        return Ok(ApiResponse<CompanyDto>.Ok(result));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ApiResponse<CompanyDto>>> Update(Guid id, [FromBody] UpdateCompanyRequest request)
    {
        var result = await companyService.UpdateAsync(id, request);
        return Ok(ApiResponse<CompanyDto>.Ok(result));
    }

    [HttpPut("{id:guid}/status")]
    public async Task<ActionResult<ApiResponse<object?>>> UpdateStatus(Guid id, [FromBody] UpdateCompanyStatusRequest request)
    {
        await companyService.UpdateStatusAsync(id, request.Status);
        return Ok(ApiResponse<object?>.Ok(null));
    }
}
