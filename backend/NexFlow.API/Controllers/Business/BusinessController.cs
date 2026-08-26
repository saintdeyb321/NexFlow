using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Features.Business;
using NexFlow.Application.Features.Business.Locations;
using NexFlow.Application.Features.Knowledge;

namespace NexFlow.API.Controllers.Business;

[ApiController]
[Route("api/business")]
[Authorize(Policy = "WorkspaceMember")]
public class BusinessController : ControllerBase
{
    private readonly IBusinessProfileRepository _profileRepository;
    private readonly IServiceRepository _serviceRepository;
    private readonly IFaqRepository _faqRepository;
    private readonly ILocationRepository _locationRepository;
    private readonly IBusinessHoursRepository _hoursRepository;
    private readonly IWorkspaceContext _workspaceContext;
    private readonly IWorkspaceRepository _workspaceRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEntitlementService _entitlementService;

    public BusinessController(
        IBusinessProfileRepository profileRepository,
        IServiceRepository serviceRepository,
        IFaqRepository faqRepository,
        ILocationRepository locationRepository,
        IBusinessHoursRepository hoursRepository,
        IWorkspaceContext workspaceContext,
        IWorkspaceRepository workspaceRepository,
        IUnitOfWork unitOfWork,
        IEntitlementService entitlementService)
    {
        _profileRepository = profileRepository;
        _serviceRepository = serviceRepository;
        _faqRepository = faqRepository;
        _locationRepository = locationRepository;
        _hoursRepository = hoursRepository;
        _workspaceContext = workspaceContext;
        _workspaceRepository = workspaceRepository;
        _unitOfWork = unitOfWork;
        _entitlementService = entitlementService;
    }

    private Guid WorkspaceId => _workspaceContext.CurrentWorkspaceId;

    private async Task<bool> HasAccessTo(string moduleCode, CancellationToken ct)
    {
        var activeModules = await _entitlementService.GetAvailableModuleCodesAsync(WorkspaceId, ct);
        return activeModules.Contains(moduleCode.ToUpperInvariant());
    }

    // 🔥 NUEVO: Endpoints de Profile para solucionar el 404 del Onboarding (Fallo Crítico #3)
    [HttpGet("profile")]
    public async Task<IActionResult> GetProfile(CancellationToken cancellationToken)
    {
        if (!await HasAccessTo("BUSINESS_PROFILE", cancellationToken)) return StatusCode(403, "Módulo BUSINESS_PROFILE no contratado.");
        var profile = await _profileRepository.GetProfileAsync(WorkspaceId, cancellationToken);

        // 🔥 CORRECCIÓN: El constructor de BusinessProfileDto exige los 5 campos obligatorios. 
        // Los enviamos vacíos para que el frontend inicie el formulario en blanco.
        return Ok(profile ?? new BusinessProfileDto(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty));
    }

    [HttpPut("profile")]
    public async Task<IActionResult> SaveProfile([FromBody] BusinessProfileDto profile, CancellationToken cancellationToken)
    {
        if (!await HasAccessTo("BUSINESS_PROFILE", cancellationToken)) return StatusCode(403, "Módulo BUSINESS_PROFILE no contratado.");
        await _profileRepository.SaveProfileAsync(WorkspaceId, profile, cancellationToken);
        return NoContent();
    }

    // --- LOCATIONS ---
    [HttpGet("locations")]
    public async Task<IActionResult> GetLocations(CancellationToken cancellationToken)
    {
        if (!await HasAccessTo("LOCATIONS", cancellationToken)) return StatusCode(403, "Módulo LOCATIONS no contratado.");
        var locations = await _locationRepository.GetLocationsAsync(WorkspaceId, cancellationToken);
        return Ok(locations);
    }

    [HttpPost("locations")]
    public async Task<IActionResult> SaveLocation(
        [FromBody] LocationDto location,
        [FromServices] SaveLocationCommandHandler handler,
        CancellationToken cancellationToken)
    {
        if (!await HasAccessTo("LOCATIONS", cancellationToken)) return StatusCode(403, "Módulo LOCATIONS no contratado.");
        var result = await handler.Handle(new SaveLocationCommand(WorkspaceId, location), cancellationToken);
        if (result.IsFailure) return StatusCode(403, result.Error);
        return Ok();
    }

    // --- HOURS ---
    [HttpGet("locations/{locationId}/hours")]
    public async Task<IActionResult> GetHours(string locationId, CancellationToken cancellationToken)
    {
        if (!await HasAccessTo("BUSINESS_HOURS", cancellationToken)) return StatusCode(403, "Módulo BUSINESS_HOURS no contratado.");
        var hours = await _hoursRepository.GetBusinessHoursAsync(WorkspaceId, locationId, cancellationToken);
        return Ok(hours);
    }

    [HttpPut("locations/{locationId}/hours")]
    public async Task<IActionResult> SaveHours(string locationId, [FromBody] BusinessHoursDto[] hours, CancellationToken cancellationToken)
    {
        if (!await HasAccessTo("BUSINESS_HOURS", cancellationToken)) return StatusCode(403, "Módulo BUSINESS_HOURS no contratado.");
        await _hoursRepository.SaveBusinessHoursAsync(WorkspaceId, locationId, hours, cancellationToken);
        return NoContent();
    }

    // --- SERVICES ---
    [HttpGet("services")]
    public async Task<IActionResult> GetServices(CancellationToken cancellationToken)
    {
        if (!await HasAccessTo("SERVICES", cancellationToken)) return StatusCode(403, "Módulo SERVICES no contratado.");
        var services = await _serviceRepository.GetServicesAsync(WorkspaceId, cancellationToken);
        return Ok(services);
    }

    [HttpPost("services")]
    public async Task<IActionResult> SaveService([FromBody] ServiceDto service, CancellationToken cancellationToken)
    {
        if (!await HasAccessTo("SERVICES", cancellationToken)) return StatusCode(403, "Módulo SERVICES no contratado.");
        await _serviceRepository.SaveServiceAsync(WorkspaceId, service, cancellationToken);
        return Ok();
    }

    [HttpDelete("services/{serviceId}")]
    public async Task<IActionResult> DeleteService(string serviceId, CancellationToken cancellationToken)
    {
        if (!await HasAccessTo("SERVICES", cancellationToken)) return StatusCode(403, "Módulo SERVICES no contratado.");
        await _serviceRepository.DeleteServiceAsync(WorkspaceId, serviceId, cancellationToken);
        return NoContent();
    }

    // --- FAQS ---
    [HttpGet("faqs")]
    public async Task<IActionResult> GetFaqs(CancellationToken cancellationToken)
    {
        if (!await HasAccessTo("FAQ", cancellationToken)) return StatusCode(403, "Módulo FAQ no contratado.");
        var faqs = await _faqRepository.GetFaqsAsync(WorkspaceId, cancellationToken);
        return Ok(faqs);
    }

    [HttpPost("faqs")]
    public async Task<IActionResult> SaveFaq([FromBody] FaqDto faq, CancellationToken cancellationToken)
    {
        if (!await HasAccessTo("FAQ", cancellationToken)) return StatusCode(403, "Módulo FAQ no contratado.");
        await _faqRepository.SaveFaqAsync(WorkspaceId, faq, cancellationToken);
        return Ok();
    }

    [HttpDelete("faqs/{faqId}")]
    public async Task<IActionResult> DeleteFaq(string faqId, CancellationToken cancellationToken)
    {
        if (!await HasAccessTo("FAQ", cancellationToken)) return StatusCode(403, "Módulo FAQ no contratado.");
        await _faqRepository.DeleteFaqAsync(WorkspaceId, faqId, cancellationToken);
        return NoContent();
    }

    // --- ONBOARDING ---
    [HttpPost("complete-onboarding")]
    public async Task<IActionResult> CompleteOnboarding(CancellationToken cancellationToken)
    {
        var workspace = await _workspaceRepository.GetByIdAsync(WorkspaceId, cancellationToken);
        if (workspace == null) return NotFound();

        workspace.Activate();
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Ok();
    }
}