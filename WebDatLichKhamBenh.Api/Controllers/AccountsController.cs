using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebDatLichKhamBenh.Application.DTOs.Accounts;
using WebDatLichKhamBenh.Application.Interfaces.Services;

namespace WebDatLichKhamBenh.Api.Controllers;

[ApiController]
[Route("api/accounts")]
[Authorize(Roles = "Admin")]
public class AccountsController : ControllerBase
{
    private readonly IAccountService _accounts;

    public AccountsController(
        IAccountService accounts)
    {
        _accounts = accounts;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => Ok(await _accounts.GetPageAsync(page, pageSize));

    [HttpPost]
    public async Task<IActionResult> Create(CreateAccountRequest request)
        => StatusCode(201, await _accounts.CreateAsync(request));

    [HttpPut("{id:guid}/access")]
    public async Task<IActionResult> UpdateAccess(Guid id, UpdateAccountAccessRequest request)
        => Ok(await _accounts.UpdateAccessAsync(id, request));
}
