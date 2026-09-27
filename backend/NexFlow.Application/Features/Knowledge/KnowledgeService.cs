using Microsoft.Extensions.Logging;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.Business.Offerings;
using System.Text;

namespace NexFlow.Application.Features.Knowledge;

public sealed class KnowledgeService : IKnowledgeService
{
    private readonly IBusinessProfileRepository _profileRepo;
    private readonly ILocationRepository _locationRepo;
    private readonly IBusinessHoursRepository _hoursRepo;
    private readonly IFaqRepository _faqRepo;
    private readonly IOfferingService _offeringService; // 🔥 Inyectamos el servicio de ofertas directamente
    private readonly ILogger<KnowledgeService> _logger;

    public KnowledgeService(
        IBusinessProfileRepository profileRepo,
        ILocationRepository locationRepo,
        IBusinessHoursRepository hoursRepo,
        IFaqRepository faqRepo,
        IOfferingService offeringService,
        ILogger<KnowledgeService> logger)
    {
        _profileRepo = profileRepo;
        _locationRepo = locationRepo;
        _hoursRepo = hoursRepo;
        _faqRepo = faqRepo;
        _offeringService = offeringService;
        _logger = logger;
    }

    public async Task<BusinessKnowledgeSnapshot> GetSnapshotAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        var profileTask = _profileRepo.GetProfileAsync(workspaceId, cancellationToken);
        var locationsTask = _locationRepo.GetLocationsAsync(workspaceId, cancellationToken);
        var hoursTask = _hoursRepo.GetBusinessHoursAsync(workspaceId, null, cancellationToken);
        var faqsTask = _faqRepo.GetFaqsAsync(workspaceId, cancellationToken);

        await Task.WhenAll(profileTask, locationsTask, hoursTask, faqsTask);

        return new BusinessKnowledgeSnapshot
        {
            WorkspaceId = workspaceId,
            Profile = profileTask.Result,
            Locations = locationsTask.Result?.ToList() ?? new(),
            Hours = hoursTask.Result?.ToList() ?? new(),
            Faqs = faqsTask.Result?.ToList() ?? new()
            // 🔥 SPRINT 03/04: Products y Services YA NO SE CARGAN EN MEMORIA.
        };
    }

    public async Task<KnowledgeResult> QueryAsync(Guid workspaceId, BusinessKnowledgeSnapshot snapshot, KnowledgeQuery query, CancellationToken cancellationToken)
    {
        var topicString = query.Topic.ToString().ToUpper();

        if (topicString == "LOCATIONS")
        {
            var data = new BusinessKnowledgeSnapshot
            {
                WorkspaceId = workspaceId,
                Locations = (await _locationRepo.GetLocationsAsync(workspaceId, cancellationToken)).ToList()
            };
            return QueryLocations(data, query.LocationId);
        }
        if (topicString == "BUSINESSHOURS")
        {
            var data = new BusinessKnowledgeSnapshot
            {
                WorkspaceId = workspaceId,
                Hours = (await _hoursRepo.GetBusinessHoursAsync(workspaceId, query.LocationId, cancellationToken)).ToList()
            };
            return QueryHours(data);
        }
        if (topicString == "FAQS")
        {
            var data = new BusinessKnowledgeSnapshot
            {
                WorkspaceId = workspaceId,
                Faqs = (await _faqRepo.GetFaqsAsync(workspaceId, cancellationToken)).ToList()
            };
            return QueryFaqs(data, query.SearchTerm);
        }
        if (topicString == "PROFILE")
        {
            var data = new BusinessKnowledgeSnapshot
            {
                WorkspaceId = workspaceId,
                Profile = await _profileRepo.GetProfileAsync(workspaceId, cancellationToken)
            };
            return QueryProfile(data);
        }

        // 🔥 Búsquedas bajo demanda directamente a BD. Protege la RAM.
        if (topicString == "PRODUCTS" || topicString == "OFFERINGS")
            return await QueryProductsAsync(workspaceId, query.LocationId, query.SearchTerm, cancellationToken);

        if (topicString == "SERVICES")
            return await QueryServicesAsync(workspaceId, query.LocationId, query.SearchTerm, cancellationToken);

        return new KnowledgeResult { Found = false, Source = query.Topic };
    }

    private static KnowledgeResult QueryLocations(BusinessKnowledgeSnapshot snapshot, string? locationId)
    {
        var locs = string.IsNullOrWhiteSpace(locationId) ? snapshot.Locations : snapshot.Locations.Where(l => l.Id == locationId).ToList();
        if (!locs.Any()) return new KnowledgeResult { Found = false };

        var sb = new StringBuilder();
        foreach (var l in locs)
        {
            sb.AppendLine($"- {l.Name} {(l.IsMain ? "(Sede Principal)" : "")}. Dirección: {l.Address}");
        }
        return new KnowledgeResult { Found = true, Facts = sb.ToString() };
    }

    private async Task<KnowledgeResult> QueryProductsAsync(Guid workspaceId, string? locationId, string? searchTerm, CancellationToken ct)
    {
        var products = await _offeringService.GetProductsAsync(workspaceId, locationId, searchTerm, ct);
        var resultList = products.Take(10).ToList();

        if (!resultList.Any()) return new KnowledgeResult { Found = false };

        var sb = new StringBuilder();
        foreach (var item in resultList)
        {
            sb.AppendLine($"- Producto: {item.Name} | Precio: {item.Currency} {item.PriceMinorUnits / 100.0m}");
            if (!string.IsNullOrWhiteSpace(item.Description)) sb.AppendLine($"  Detalle: {item.Description}");
        }
        return new KnowledgeResult { Found = true, Facts = sb.ToString() };
    }

    private async Task<KnowledgeResult> QueryServicesAsync(Guid workspaceId, string? locationId, string? searchTerm, CancellationToken ct)
    {
        var services = await _offeringService.GetServicesAsync(workspaceId, locationId, searchTerm, ct);
        var resultList = services.Take(10).ToList();

        if (!resultList.Any()) return new KnowledgeResult { Found = false };

        var sb = new StringBuilder();
        foreach (var item in resultList)
        {
            sb.AppendLine($"- Servicio: {item.Name} | Precio: {item.Currency} {item.PriceMinorUnits / 100.0m}");
            if (item.DurationInMinutes.HasValue) sb.AppendLine($"  Duración: {item.DurationInMinutes} min");
            if (!string.IsNullOrWhiteSpace(item.Description)) sb.AppendLine($"  Detalle: {item.Description}");
        }
        return new KnowledgeResult { Found = true, Facts = sb.ToString() };
    }

    private static KnowledgeResult QueryHours(BusinessKnowledgeSnapshot snapshot)
    {
        if (!snapshot.Hours.Any()) return new KnowledgeResult { Found = false };
        var sb = new StringBuilder();
        foreach (var h in snapshot.Hours.OrderBy(x => x.DayOfWeek))
        {
            sb.AppendLine(h.IsClosed ? $"- Día {h.DayOfWeek}: Cerrado" : $"- Día {h.DayOfWeek}: {h.OpenTime} a {h.CloseTime}");
        }
        return new KnowledgeResult { Found = true, Facts = sb.ToString() };
    }

    private static KnowledgeResult QueryFaqs(BusinessKnowledgeSnapshot snapshot, string? searchTerm)
    {
        var faqs = snapshot.Faqs.Where(f => f.IsActive);
        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.ToLowerInvariant();
            faqs = faqs.Where(f => f.Question.ToLowerInvariant().Contains(term) || f.Answer.ToLowerInvariant().Contains(term));
        }
        var resultList = faqs.Take(5).ToList();
        if (!resultList.Any()) return new KnowledgeResult { Found = false };

        var sb = new StringBuilder();
        foreach (var f in resultList) sb.AppendLine($"P: {f.Question}\nR: {f.Answer}\n");
        return new KnowledgeResult { Found = true, Facts = sb.ToString() };
    }

    private static KnowledgeResult QueryProfile(BusinessKnowledgeSnapshot snapshot)
    {
        return snapshot.Profile == null
            ? new KnowledgeResult { Found = false }
            : new KnowledgeResult { Found = true, Facts = snapshot.Profile.Description ?? "Sin descripción" };
    }
}
