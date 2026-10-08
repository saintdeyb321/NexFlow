using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Integrations;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Features.Business;
using NexFlow.Application.Features.Business.Locations;
using NexFlow.Application.Features.Knowledge;
using NexFlow.Domain.Enums;
using NexFlow.API.Middleware;

namespace NexFlow.API.Controllers.Business;

[ApiController]
[Route("api/business")]
[Authorize(Policy = "WorkspaceMember")]
public class BusinessController : ControllerBase
{
    private readonly IBusinessProfileRepository _profileRepository;
    private readonly ICatalogRepository _catalogRepository;
    private readonly IFaqRepository _faqRepository;
    private readonly ILocationRepository _locationRepository;
    private readonly IBusinessHoursRepository _hoursRepository;
    private readonly IWorkspaceContext _workspaceContext;
    private readonly IWorkspaceRepository _workspaceRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEntitlementService _entitlementService;

    public BusinessController(
        IBusinessProfileRepository profileRepository,
        ICatalogRepository catalogRepository,
        IFaqRepository faqRepository,
        ILocationRepository locationRepository,
        IBusinessHoursRepository hoursRepository,
        IWorkspaceContext workspaceContext,
        IWorkspaceRepository workspaceRepository,
        IUnitOfWork unitOfWork,
        IEntitlementService entitlementService)
    {
        _profileRepository = profileRepository;
        _catalogRepository = catalogRepository;
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

    [HttpGet("profile")]
    public async Task<IActionResult> GetProfile(CancellationToken cancellationToken)
    {
        if (!await HasAccessTo("BUSINESS_PROFILE", cancellationToken)) return StatusCode(403, "Módulo BUSINESS_PROFILE no contratado.");
        var profile = await _profileRepository.GetProfileAsync(WorkspaceId, cancellationToken);
        return Ok(profile ?? new BusinessProfileDto(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty));
    }

    [HttpPut("profile")]
    public async Task<IActionResult> SaveProfile([FromBody] BusinessProfileDto profile, CancellationToken cancellationToken)
    {
        if (!await HasAccessTo("BUSINESS_PROFILE", cancellationToken)) return StatusCode(403, "Módulo BUSINESS_PROFILE no contratado.");

        await _profileRepository.SaveProfileAsync(WorkspaceId, profile, cancellationToken);

        var workspace = await _workspaceRepository.GetByIdAsync(WorkspaceId, cancellationToken);
        if (workspace != null)
        {
            if (!string.IsNullOrWhiteSpace(profile.CommercialName) && workspace.Name != profile.CommercialName)
            {
                workspace.Rename(profile.CommercialName);
            }
            if (workspace.Status == WorkspaceStatus.Pending)
            {
                workspace.Activate();
            }
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

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

        var result = await handler.Handle(new SaveLocationCommand(WorkspaceId, location, IsCreate: true), cancellationToken);
        if (result.IsFailure) return LocationError(result.Error);
        return Ok();
    }

    [HttpPut("locations/{locationId}")]
    public async Task<IActionResult> UpdateLocation(
        string locationId,
        [FromBody] LocationDto location,
        [FromServices] SaveLocationCommandHandler handler,
        CancellationToken cancellationToken)
    {
        if (!await HasAccessTo("LOCATIONS", cancellationToken)) return StatusCode(403, "Módulo LOCATIONS no contratado.");

        location = location with { Id = locationId };
        var result = await handler.Handle(new SaveLocationCommand(WorkspaceId, location, IsCreate: false), cancellationToken);
        if (result.IsFailure) return LocationError(result.Error);
        return Ok();
    }

    [HttpDelete("locations/{locationId}")]
    public async Task<IActionResult> DeleteLocation(
        string locationId,
        [FromServices] IReservationRepository reservationRepository,
        CancellationToken cancellationToken)
    {
        if (!await HasAccessTo("LOCATIONS", cancellationToken)) return StatusCode(403, "Módulo LOCATIONS no contratado.");

        if (await reservationRepository.HasFutureConfirmedAtLocationAsync(WorkspaceId, locationId, cancellationToken))
            return Conflict(new { message = "No puedes eliminar una sede con reservas futuras confirmadas." });
        // References, hours and the replacement main location are handled atomically.
        var result = await _locationRepository.DeleteLocationAsync(WorkspaceId, locationId, cancellationToken);
        if (result.IsFailure) return LocationError(result.Error);
        return NoContent();
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
        try { await _hoursRepository.SaveBusinessHoursAsync(WorkspaceId, locationId, hours, cancellationToken); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (NexFlow.Domain.Exceptions.DomainException ex) { return BadRequest(new { message = ex.Message }); }
        return NoContent();
    }

    // --- FAQS ---
    [HttpGet("faqs")]
    public async Task<IActionResult> GetFaqs([FromQuery] string? locationId, CancellationToken cancellationToken)
    {
        if (!await HasAccessTo("FAQ", cancellationToken)) return StatusCode(403, "Módulo FAQ no contratado.");
        var faqs = await _faqRepository.GetFaqsAsync(WorkspaceId, cancellationToken);
        return Ok(faqs);
    }

    [HttpPost("faqs")]
    public async Task<IActionResult> SaveFaq([FromBody] FaqDto faq, CancellationToken cancellationToken)
    {
        if (!await HasAccessTo("FAQ", cancellationToken)) return StatusCode(403, "Módulo FAQ no contratado.");

        faq.Id = Guid.NewGuid().ToString();
        var currentFaqs = await _faqRepository.GetFaqsAsync(WorkspaceId, cancellationToken);
        if (currentFaqs.Count() >= 20)
        {
            return BadRequest(new { code = "Limit.Exceeded", message = "Has alcanzado el límite máximo de 20 preguntas frecuentes. Elimina una antigua para agregar una nueva." });
        }

        var savedFaq = await _faqRepository.SaveFaqAsync(WorkspaceId, faq, true, cancellationToken);
        return Ok(savedFaq);
    }

    [HttpPut("faqs/{faqId}")]
    public async Task<IActionResult> UpdateFaq(string faqId, [FromBody] FaqDto faq, CancellationToken cancellationToken)
    {
        if (!await HasAccessTo("FAQ", cancellationToken)) return StatusCode(403, "Módulo FAQ no contratado.");
        faq.Id = faqId;
        var savedFaq = await _faqRepository.SaveFaqAsync(WorkspaceId, faq, false, cancellationToken);
        return Ok(savedFaq);
    }

    [HttpDelete("faqs/{faqId}")]
    public async Task<IActionResult> DeleteFaq(string faqId, CancellationToken cancellationToken)
    {
        if (!await HasAccessTo("FAQ", cancellationToken)) return StatusCode(403, "Módulo FAQ no contratado.");
        await _faqRepository.DeleteFaqAsync(WorkspaceId, faqId, cancellationToken);
        return NoContent();
    }

    // =======================================================
    // WHATSAPP (Evolution API)
    // =======================================================
    [HttpGet("whatsapp/status")]
    [ReleaseTenantLifecycleLock]
    public async Task<IActionResult> GetWhatsAppStatus(
        [FromQuery] bool refresh,
        [FromServices] IEvolutionConnectionService evolutionService,
        CancellationToken cancellationToken)
    {
        if (WorkspaceId == Guid.Empty || !await _entitlementService.HasCapabilityAccessAsync(WorkspaceId, "CONVERSATIONS", "READ", cancellationToken)) return StatusCode(403);
        var status = await evolutionService.GetStatusAsync(WorkspaceId, refresh, cancellationToken);
        if (!await _entitlementService.HasCapabilityAccessAsync(WorkspaceId, "CONVERSATIONS", "CONFIGURE", cancellationToken))
            status = status with { QrBase64 = null, CanConnect = false };
        return Ok(status);
    }

    [HttpPost("whatsapp/connect")]
    [ReleaseTenantLifecycleLock]
    public async Task<IActionResult> ConnectWhatsApp(
        [FromServices] IEvolutionConnectionService evolutionService,
        CancellationToken cancellationToken)
    {
        if (WorkspaceId == Guid.Empty || !await _entitlementService.HasCapabilityAccessAsync(WorkspaceId, "CONVERSATIONS", "CONFIGURE", cancellationToken)) return StatusCode(403);
        return Ok(await evolutionService.ConnectAsync(WorkspaceId, cancellationToken));
    }

    [HttpPost("whatsapp/disconnect")]
    [ReleaseTenantLifecycleLock]
    public async Task<IActionResult> DisconnectWhatsApp(
        [FromBody] DisconnectWhatsAppRequest request,
        [FromServices] IEvolutionConnectionService evolutionService,
        CancellationToken cancellationToken)
    {
        if (WorkspaceId == Guid.Empty || !await _entitlementService.HasCapabilityAccessAsync(WorkspaceId, "CONVERSATIONS", "CONFIGURE", cancellationToken)) return StatusCode(403);
        return Ok(await evolutionService.DisconnectAsync(WorkspaceId, request.Confirmed, cancellationToken));
    }
    private IActionResult LocationError(NexFlow.Application.Common.Error error) =>
        StatusCode(error.Code switch
        {
            "Location.NotFound" => 404,
            "Location.Conflict" => 409,
            "Location.LimitReached" => 403,
            _ => 400
        }, new { code = error.Code, message = error.Description });
}

public sealed record DisconnectWhatsAppRequest(bool Confirmed);

