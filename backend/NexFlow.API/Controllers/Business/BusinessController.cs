using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Features.Business;
using NexFlow.Application.Features.Business.Locations;
using NexFlow.Application.Features.Knowledge;
using NexFlow.Domain.Enums;
using System.Linq;

namespace NexFlow.API.Controllers.Business;

[ApiController]
[Route("api/business")]
[Authorize(Policy = "WorkspaceMember")]
public class BusinessController : ControllerBase
{
    private readonly IBusinessProfileRepository _profileRepository;
    private readonly ICatalogRepository _catalogRepository; // 🔥 SPRINT 3: Reemplaza a IServiceRepository
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

        var currentLocations = await _locationRepository.GetLocationsAsync(WorkspaceId, cancellationToken);
        bool isNewLocation = string.IsNullOrEmpty(location.Id) || !currentLocations.Any(l => l.Id == location.Id);

        if (isNewLocation)
        {
            int maxLocations = await _entitlementService.GetMaxLocationsAsync(WorkspaceId, cancellationToken);
            if (currentLocations.Count() >= maxLocations)
            {
                return StatusCode(403, new { code = "BusinessRuleViolation", message = $"Has alcanzado el límite máximo de {maxLocations} sede(s) permitido por tu plan actual." });
            }
        }

        var result = await handler.Handle(new SaveLocationCommand(WorkspaceId, location), cancellationToken);
        if (result.IsFailure) return StatusCode(400, new { message = result.Error });

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

        var currentLocations = await _locationRepository.GetLocationsAsync(WorkspaceId, cancellationToken);
        bool exists = currentLocations.Any(l => l.Id == locationId);

        if (!exists)
        {
            int maxLocations = await _entitlementService.GetMaxLocationsAsync(WorkspaceId, cancellationToken);
            if (currentLocations.Count() >= maxLocations)
            {
                return StatusCode(403, new { code = "BusinessRuleViolation", message = $"Has alcanzado el límite máximo de {maxLocations} sede(s) permitido por tu plan actual." });
            }
        }

        location = location with { Id = locationId };

        var result = await handler.Handle(new SaveLocationCommand(WorkspaceId, location), cancellationToken);
        if (result.IsFailure) return StatusCode(400, new { message = result.Error });

        return Ok();
    }

    [HttpDelete("locations/{locationId}")]
    public async Task<IActionResult> DeleteLocation(string locationId, CancellationToken cancellationToken)
    {
        if (!await HasAccessTo("LOCATIONS", cancellationToken)) return StatusCode(403, "Módulo LOCATIONS no contratado.");

        // 🔥 SPRINT 3: Limpieza en Cascada Universal (limpia productos y servicios a la vez)
        var items = await _catalogRepository.GetActiveItemsAsync(WorkspaceId, cancellationToken);
        var affectedItems = items.Where(i => i.AvailableAtLocations != null && i.AvailableAtLocations.Contains(locationId)).ToList();

        foreach (var item in affectedItems)
        {
            item.AvailableAtLocations.Remove(locationId);
            await _catalogRepository.SaveItemAsync(WorkspaceId, item, cancellationToken);
        }

        await _hoursRepository.SaveBusinessHoursAsync(WorkspaceId, locationId, Array.Empty<BusinessHoursDto>(), cancellationToken);
        await _locationRepository.DeleteLocationAsync(WorkspaceId, locationId, cancellationToken);

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
        await _hoursRepository.SaveBusinessHoursAsync(WorkspaceId, locationId, hours, cancellationToken);
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

        if (string.IsNullOrEmpty(faq.Id))
        {
            var currentFaqs = await _faqRepository.GetFaqsAsync(WorkspaceId, cancellationToken);
            if (currentFaqs.Count() >= 20)
            {
                return BadRequest(new { code = "Limit.Exceeded", message = "Has alcanzado el límite máximo de 20 preguntas frecuentes. Elimina una antigua para agregar una nueva." });
            }
            faq.Id = Guid.NewGuid().ToString();
        }

        var savedFaq = await _faqRepository.SaveFaqAsync(WorkspaceId, faq, cancellationToken);
        return Ok(savedFaq);
    }

    [HttpPut("faqs/{faqId}")]
    public async Task<IActionResult> UpdateFaq(string faqId, [FromBody] FaqDto faq, CancellationToken cancellationToken)
    {
        if (!await HasAccessTo("FAQ", cancellationToken)) return StatusCode(403, "Módulo FAQ no contratado.");
        faq.Id = faqId;
        var savedFaq = await _faqRepository.SaveFaqAsync(WorkspaceId, faq, cancellationToken);
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
    // SERVICES (Módulo Licenciado Separadamente)
    // =======================================================
    [HttpGet("services")]
    public async Task<IActionResult> GetServices(CancellationToken cancellationToken)
    {
        if (!await HasAccessTo("SERVICES", cancellationToken)) return StatusCode(403, "Módulo SERVICES no contratado.");

        var allItems = await _catalogRepository.GetItemsAsync(WorkspaceId, cancellationToken);
        var services = allItems.Where(i => i.Type.ToUpperInvariant() == "SERVICE").ToList();

        return Ok(services);
    }

    [HttpPost("services")]
    public async Task<IActionResult> SaveService([FromBody] CatalogItemDto service, CancellationToken cancellationToken)
    {
        if (!await HasAccessTo("SERVICES", cancellationToken)) return StatusCode(403, "Módulo SERVICES no contratado.");

        // 🔥 ESTRICTO: Obligamos a que sea SERVICE
        service.Type = "SERVICE";
        if (string.IsNullOrEmpty(service.Id)) service.Id = Guid.NewGuid().ToString();

        // Si la UI de servicios aún no maneja categorías, le damos una por defecto
        if (string.IsNullOrEmpty(service.CategoryId)) service.CategoryId = Guid.Empty.ToString();

        await _catalogRepository.SaveItemAsync(WorkspaceId, service, cancellationToken);
        return Ok(service);
    }

    [HttpDelete("services/{serviceId}")]
    public async Task<IActionResult> DeleteService(string serviceId, CancellationToken cancellationToken)
    {
        if (!await HasAccessTo("SERVICES", cancellationToken)) return StatusCode(403, "Módulo SERVICES no contratado.");

        // 🔥 ESTRICTO: Solo borramos si el item es un SERVICIO
        var item = await _catalogRepository.GetItemByIdAsync(WorkspaceId, serviceId, cancellationToken);
        if (item != null && item.Type.ToUpperInvariant() == "SERVICE")
        {
            await _catalogRepository.DeleteItemAsync(WorkspaceId, serviceId, cancellationToken);
        }

        return NoContent();
    }
}