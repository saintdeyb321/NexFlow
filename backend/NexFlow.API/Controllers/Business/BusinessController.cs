using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.Business;
using NexFlow.Application.Features.Knowledge;

namespace NexFlow.API.Controllers.Business;

[ApiController]
[Route("api/workspaces/{workspaceId}/business")]
[Authorize(Policy = "WorkspaceMember")]
public class BusinessController : ControllerBase
{
    private readonly IBusinessProfileRepository _profileRepository;
    private readonly IServiceRepository _serviceRepository;
    private readonly IFaqRepository _faqRepository;

    public BusinessController(
        IBusinessProfileRepository profileRepository,
        IServiceRepository serviceRepository,
        IFaqRepository faqRepository)
    {
        _profileRepository = profileRepository;
        _serviceRepository = serviceRepository;
        _faqRepository = faqRepository;
    }

    [HttpGet("profile")]
    public async Task<IActionResult> GetProfile(Guid workspaceId, CancellationToken cancellationToken)
    {
        var profile = await _profileRepository.GetProfileAsync(workspaceId, cancellationToken);
        return profile != null ? Ok(profile) : NotFound();
    }

    [HttpPut("profile")]
    public async Task<IActionResult> SaveProfile(Guid workspaceId, [FromBody] BusinessProfileDto profile, CancellationToken cancellationToken)
    {
        await _profileRepository.SaveProfileAsync(workspaceId, profile, cancellationToken);
        return NoContent();
    }

    [HttpGet("services")]
    public async Task<IActionResult> GetServices(Guid workspaceId, CancellationToken cancellationToken)
    {
        var services = await _serviceRepository.GetServicesAsync(workspaceId, cancellationToken);
        return Ok(services);
    }

    [HttpPost("services")]
    public async Task<IActionResult> SaveService(Guid workspaceId, [FromBody] ServiceDto service, CancellationToken cancellationToken)
    {
        await _serviceRepository.SaveServiceAsync(workspaceId, service, cancellationToken);
        return Ok();
    }

    [HttpDelete("services/{serviceId}")]
    public async Task<IActionResult> DeleteService(Guid workspaceId, string serviceId, CancellationToken cancellationToken)
    {
        await _serviceRepository.DeleteServiceAsync(workspaceId, serviceId, cancellationToken);
        return NoContent();
    }

    [HttpGet("faqs")]
    public async Task<IActionResult> GetFaqs(Guid workspaceId, CancellationToken cancellationToken)
    {
        var faqs = await _faqRepository.GetFaqsAsync(workspaceId, cancellationToken);
        return Ok(faqs);
    }

    [HttpPost("faqs")]
    public async Task<IActionResult> SaveFaq(Guid workspaceId, [FromBody] FaqDto faq, CancellationToken cancellationToken)
    {
        await _faqRepository.SaveFaqAsync(workspaceId, faq, cancellationToken);
        return Ok();
    }

    [HttpDelete("faqs/{faqId}")]
    public async Task<IActionResult> DeleteFaq(Guid workspaceId, string faqId, CancellationToken cancellationToken)
    {
        await _faqRepository.DeleteFaqAsync(workspaceId, faqId, cancellationToken);
        return NoContent();
    }
}