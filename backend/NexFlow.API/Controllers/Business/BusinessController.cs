using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.Business;
using NexFlow.Application.Features.Knowledge;
using NexFlow.Application.Features.Reservations;

namespace NexFlow.API.Controllers.Business;

[ApiController]
[Route("api/workspaces/{workspaceId}/business")]
[Authorize(Policy = "WorkspaceMember")] // <- Solo miembros verificados en PostgreSQL pueden pasar
public class BusinessController : ControllerBase
{
    private readonly IBusinessConfigurationRepository _businessRepository;

    public BusinessController(IBusinessConfigurationRepository businessRepository)
    {
        _businessRepository = businessRepository;
    }

    // --- PROFILE ---
    [HttpGet("profile")]
    public async Task<IActionResult> GetProfile(Guid workspaceId, CancellationToken cancellationToken)
    {
        var profile = await _businessRepository.GetProfileAsync(workspaceId, cancellationToken);
        return profile != null ? Ok(profile) : NotFound();
    }

    [HttpPut("profile")]
    public async Task<IActionResult> SaveProfile(Guid workspaceId, [FromBody] BusinessProfileDto profile, CancellationToken cancellationToken)
    {
        await _businessRepository.SaveProfileAsync(workspaceId, profile, cancellationToken);
        return NoContent();
    }

    // --- SERVICES ---
    [HttpGet("services")]
    public async Task<IActionResult> GetServices(Guid workspaceId, CancellationToken cancellationToken)
    {
        var services = await _businessRepository.GetServicesAsync(workspaceId, cancellationToken);
        return Ok(services);
    }

    [HttpPost("services")]
    public async Task<IActionResult> SaveService(Guid workspaceId, [FromBody] ServiceDto service, CancellationToken cancellationToken)
    {
        await _businessRepository.SaveServiceAsync(workspaceId, service, cancellationToken);
        return Ok();
    }

    [HttpDelete("services/{serviceId}")]
    public async Task<IActionResult> DeleteService(Guid workspaceId, string serviceId, CancellationToken cancellationToken)
    {
        await _businessRepository.DeleteServiceAsync(workspaceId, serviceId, cancellationToken);
        return NoContent();
    }

    // --- FAQS ---
    [HttpGet("faqs")]
    public async Task<IActionResult> GetFaqs(Guid workspaceId, CancellationToken cancellationToken)
    {
        var faqs = await _businessRepository.GetFaqsAsync(workspaceId, cancellationToken);
        return Ok(faqs);
    }

    [HttpPost("faqs")]
    public async Task<IActionResult> SaveFaq(Guid workspaceId, [FromBody] FaqDto faq, CancellationToken cancellationToken)
    {
        await _businessRepository.SaveFaqAsync(workspaceId, faq, cancellationToken);
        return Ok();
    }

    [HttpDelete("faqs/{faqId}")]
    public async Task<IActionResult> DeleteFaq(Guid workspaceId, string faqId, CancellationToken cancellationToken)
    {
        await _businessRepository.DeleteFaqAsync(workspaceId, faqId, cancellationToken);
        return NoContent();
    }
}