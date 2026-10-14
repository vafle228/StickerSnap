using Microsoft.AspNetCore.Mvc;
using StickerSnap.Api.Contracts;
using StickerSnap.Application.Account;

namespace StickerSnap.Api.Controllers;

/// <summary>/api/v1/account of group account: linking the Telegram account to the bot (artboard 5).</summary>
[ApiController]
[Route("api/v1/account")]
[Tags("account")]
[Produces("application/json")]
public sealed class AccountController : ControllerBase
{
    private readonly IAccountService _service;

    public AccountController(IAccountService service)
    {
        _service = service;
    }

    /// <summary>Not linked, waiting for Start, linked or an error.</summary>
    [HttpGet(Name = "getAccount")]
    [ProducesResponseType(typeof(AccountLink), StatusCodes.Status200OK)]
    [ProducesDefaultResponseType(typeof(ProblemDetails))]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) =>
        Ok(AccountLink.From(await _service.GetAsync(cancellationToken)));

    /// <summary>A one-time code in a link to the bot; Start pressed comes as accountChanged.</summary>
    [HttpPost("link", Name = "startAccountLink")]
    [ProducesResponseType(typeof(AccountLink), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    [ProducesDefaultResponseType(typeof(ProblemDetails))]
    public async Task<IActionResult> StartLink(CancellationToken cancellationToken) =>
        Ok(AccountLink.From(await _service.StartLinkAsync(cancellationToken)));

    /// <summary>«Отмена» while waiting for Start, otherwise «Отвязать».</summary>
    [HttpDelete("link", Name = "deleteAccountLink")]
    [ProducesResponseType(typeof(AccountLink), StatusCodes.Status200OK)]
    [ProducesDefaultResponseType(typeof(ProblemDetails))]
    public async Task<IActionResult> DeleteLink(CancellationToken cancellationToken) =>
        Ok(AccountLink.From(await _service.CancelOrUnlinkAsync(cancellationToken)));

    /// <summary>«Проверить связь»: whether the bot can still act for the user.</summary>
    [HttpPost("check", Name = "checkAccount")]
    [ProducesResponseType(typeof(AccountLink), StatusCodes.Status200OK)]
    [ProducesDefaultResponseType(typeof(ProblemDetails))]
    public async Task<IActionResult> Check(CancellationToken cancellationToken) =>
        Ok(AccountLink.From(await _service.CheckAsync(cancellationToken)));
}
