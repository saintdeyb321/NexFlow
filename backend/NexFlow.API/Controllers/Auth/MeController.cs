using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexFlow.Application.Features.Identity.GetMe;

namespace NexFlow.API.Controllers.Auth;

[ApiController]
[Route("api/me")]
[Authorize] // Pide token JWT validado por Firebase y Middleware
public class MeController : ControllerBase
{
    private readonly GetMeQueryHandler _handler;

    public MeController(GetMeQueryHandler handler)
    {
        _handler = handler;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var result = await _handler.Handle(cancellationToken);

        if (result.IsFailure) return Unauthorized(result.Error);

        return Ok(result.Value);
    }
}