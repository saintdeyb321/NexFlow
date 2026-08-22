using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Features.Business;
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

    public BusinessController(
        IBusinessProfileRepository profileRepository,
        IServiceRepository serviceRepository,
        IFaqRepository faqRepository,
        ILocationRepository locationRepository,
        IBusinessHoursRepository hoursRepository,
        IWorkspaceContext workspaceContext,
        IWorkspaceRepository workspaceRepository,
        IUnitOfWork unitOfWork)
    {
        _profileRepository = profileRepository;
        _serviceRepository = serviceRepository;
        _faqRepository = faqRepository;
        _locationRepository = locationRepository;
        _hoursRepository = hoursRepository;
        _workspaceContext = workspaceContext;
        _workspaceRepository = workspaceRepository;
        _unitOfWork = unitOfWork;
    }

    private Guid WorkspaceId => _workspaceContext.CurrentWorkspaceId;

    // --- PROFILE ---
    [HttpGet("profile")]
    public async Task<IActionResult> GetProfile(CancellationToken cancellationToken)
    {
        var profile = await _profileRepository.GetProfileAsync(WorkspaceId, cancellationToken);
        return profile != null ? Ok(profile) : NotFound();
    }

    [HttpPut("profile")]
    public async Task<IActionResult> SaveProfile([FromBody] BusinessProfileDto profile, CancellationToken cancellationToken)
    {
        await _profileRepository.SaveProfileAsync(WorkspaceId, profile, cancellationToken);
        return NoContent();
    }

    // --- LOCATIONS (NUEVO - Soluciona el 404) ---
    [HttpGet("locations")]
    public async Task<IActionResult> GetLocations(CancellationToken cancellationToken)
    {
        var locations = await _locationRepository.GetLocationsAsync(WorkspaceId, cancellationToken);
        return Ok(locations);
    }

    [HttpPost("locations")]
    public async Task<IActionResult> SaveLocation([FromBody] LocationDto location, CancellationToken cancellationToken)
    {
        await _locationRepository.SaveLocationAsync(WorkspaceId, location, cancellationToken);
        return Ok();
    }

    // --- HOURS (NUEVO - Soluciona el 404) ---
    [HttpGet("hours")]
    public async Task<IActionResult> GetHours(CancellationToken cancellationToken)
    {
        // Obtenemos la sede principal para consultar sus horarios
        var locations = await _locationRepository.GetLocationsAsync(WorkspaceId, cancellationToken);
        var mainLoc = locations.FirstOrDefault(l => l.IsMain) ?? locations.FirstOrDefault();
        var locationId = mainLoc?.Id ?? "default";

        var hours = await _hoursRepository.GetBusinessHoursAsync(WorkspaceId, locationId, cancellationToken);
        return Ok(hours);
    }

    [HttpPut("hours")]
    public async Task<IActionResult> SaveHours([FromBody] BusinessHoursDto[] hours, CancellationToken cancellationToken)
    {
        var locations = await _locationRepository.GetLocationsAsync(WorkspaceId, cancellationToken);
        var mainLoc = locations.FirstOrDefault(l => l.IsMain) ?? locations.FirstOrDefault();
        var locationId = mainLoc?.Id ?? "default";

        await _hoursRepository.SaveBusinessHoursAsync(WorkspaceId, locationId, hours, cancellationToken);
        return NoContent();
    }

    // --- SERVICES ---
    [HttpGet("services")]
    public async Task<IActionResult> GetServices(CancellationToken cancellationToken)
    {
        var services = await _serviceRepository.GetServicesAsync(WorkspaceId, cancellationToken);
        return Ok(services);
    }

    [HttpPost("services")]
    public async Task<IActionResult> SaveService([FromBody] ServiceDto service, CancellationToken cancellationToken)
    {
        await _serviceRepository.SaveServiceAsync(WorkspaceId, service, cancellationToken);
        return Ok();
    }

    [HttpDelete("services/{serviceId}")]
    public async Task<IActionResult> DeleteService(string serviceId, CancellationToken cancellationToken)
    {
        await _serviceRepository.DeleteServiceAsync(WorkspaceId, serviceId, cancellationToken);
        return NoContent();
    }

    // --- FAQS ---
    [HttpGet("faqs")]
    public async Task<IActionResult> GetFaqs(CancellationToken cancellationToken)
    {
        var faqs = await _faqRepository.GetFaqsAsync(WorkspaceId, cancellationToken);
        return Ok(faqs);
    }

    [HttpPost("faqs")]
    public async Task<IActionResult> SaveFaq([FromBody] FaqDto faq, CancellationToken cancellationToken)
    {
        await _faqRepository.SaveFaqAsync(WorkspaceId, faq, cancellationToken);
        return Ok();
    }

    [HttpDelete("faqs/{faqId}")]
    public async Task<IActionResult> DeleteFaq(string faqId, CancellationToken cancellationToken)
    {
        await _faqRepository.DeleteFaqAsync(WorkspaceId, faqId, cancellationToken);
        return NoContent();
    }

    [HttpPost("complete-onboarding")]
    public async Task<IActionResult> CompleteOnboarding(CancellationToken cancellationToken)
    {
        var workspace = await _workspaceRepository.GetByIdAsync(WorkspaceId, cancellationToken);
        if (workspace == null) return NotFound();

        // Cambiamos el estado a Active en PostgreSQL
        workspace.Activate();

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Ok();
    }
}