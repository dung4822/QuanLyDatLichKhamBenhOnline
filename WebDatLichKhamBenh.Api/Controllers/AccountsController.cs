using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebDatLichKhamBenh.Application.DTOs.Accounts;
using WebDatLichKhamBenh.Application.Interfaces.Services;

namespace WebDatLichKhamBenh.Api.Controllers;

[ApiController]
[Route("api/accounts")]
[Authorize(Roles = "Admin")]
public class AccountsController(IAccountService accounts) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => Ok(await accounts.GetPageAsync(page, pageSize));

    [HttpPost]
    public async Task<IActionResult> Create(CreateAccountRequest request)
        => StatusCode(201, await accounts.CreateAsync(request));

    [HttpPut("{id:guid}/access")]
    public async Task<IActionResult> UpdateAccess(Guid id, UpdateAccountAccessRequest request)
        => Ok(await accounts.UpdateAccessAsync(id, request));
}
