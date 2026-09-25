using System.Text;
using Microsoft.Extensions.Logging;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.Shared.DTOs;
using NexFlow.Application.Features.Catalog.DTOs;
using NexFlow.Application.Features.Services.DTOs;

namespace NexFlow.Application.Features.Knowledge;

public sealed class KnowledgeService : IKnowledgeService
{
    private readonly IBusinessProfileRepository _profileRepo;
    private readonly ILocationRepository _locationRepo;
    private readonly IBusinessHoursRepository _hoursRepo;
    private readonly IFaqRepository _faqRepo;
    private readonly ICatalogRepository _catalogRepo;
    private readonly ILogger<KnowledgeService> _logger;

    public KnowledgeService(
        IBusinessProfileRepository profileRepo,
        ILocationRepository locationRepo,
        IBusinessHoursRepository hoursRepo,
        IFaqRepository faqRepo,
        ICatalogRepository catalogRepo,
        ILogger<KnowledgeService> logger)
    {
        _profileRepo = profileRepo;
        _locationRepo = locationRepo;
        _hoursRepo = hoursRepo;
        _faqRepo = faqRepo;
        _catalogRepo = catalogRepo;
        _logger = logger;
    }

    public async Task<BusinessKnowledgeSnapshot> GetSnapshotAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        var profileTask = _profileRepo.GetProfileAsync(workspaceId, cancellationToken);
        var locationsTask = _locationRepo.GetLocationsAsync(workspaceId, cancellationToken);
        var hoursTask = _hoursRepo.GetBusinessHoursAsync(workspaceId, null, cancellationToken);
        var faqsTask = _faqRepo.GetFaqsAsync(workspaceId, cancellationToken);
        var catalogTask = _catalogRepo.GetActiveItemsAsync(workspaceId, cancellationToken);

        await Task.WhenAll(profileTask, locationsTask, hoursTask, faqsTask, catalogTask);

        var allItems = catalogTask.Result ?? new List<BusinessOfferingDto>();

        return new BusinessKnowledgeSnapshot
        {
            WorkspaceId = workspaceId,
            Profile = profileTask.Result,
            Locations = locationsTask.Result?.ToList() ?? new(),
            Hours = hoursTask.Result?.ToList() ?? new(),
            Faqs = faqsTask.Result?.ToList() ?? new(),
            // 🔥 SPRINT 04: Casteo y separación limpia desde la BD
            Products = allItems.Where(i => i.Type == "PRODUCT").Select(i => (ProductDto)i).ToList(),
            Services = allItems.Where(i => i.Type == "SERVICE").Select(i => (ServiceDto)i).ToList()
        };
    }

    // Nota: Dependiendo de tu definición de KnowledgeQuery, ajusta los Topics
    public KnowledgeResult Query(BusinessKnowledgeSnapshot snapshot, KnowledgeQuery query)
    {
        // Se asume que actualizaste el Enum KnowledgeTopic para incluir Products y Services
        var topicString = query.Topic.ToString().ToUpper();

        if (topicString == "LOCATIONS") return QueryLocations(snapshot, query.LocationId);
        if (topicString == "PRODUCTS" || topicString == "OFFERINGS") return QueryProducts(snapshot, query.LocationId, query.SearchTerm);
        if (topicString == "SERVICES") return QueryServices(snapshot, query.LocationId, query.SearchTerm);
        if (topicString == "BUSINESSHOURS") return QueryHours(snapshot);
        if (topicString == "FAQS") return QueryFaqs(snapshot, query.SearchTerm);
        if (topicString == "PROFILE") return QueryProfile(snapshot);

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

    private static KnowledgeResult QueryProducts(BusinessKnowledgeSnapshot snapshot, string? locationId, string? searchTerm)
    {
        var items = snapshot.Products.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(locationId))
            items = items.Where(i => i.LocationScope == "ALL" || (i.LocationIds != null && i.LocationIds.Contains(locationId)));

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.ToLowerInvariant();
            items = items.Where(i => i.Name.ToLowerInvariant().Contains(term) || (i.Description != null && i.Description.ToLowerInvariant().Contains(term)));
        }

        var resultList = items.Take(10).ToList();
        if (!resultList.Any()) return new KnowledgeResult { Found = false };

        var sb = new StringBuilder();
        foreach (var item in resultList)
        {
            sb.AppendLine($"- Producto: {item.Name} | Precio: {item.Currency} {item.PriceMinorUnits / 100.0m}");
            if (!string.IsNullOrWhiteSpace(item.Description)) sb.AppendLine($"  Detalle: {item.Description}");
        }
        return new KnowledgeResult { Found = true, Facts = sb.ToString() };
    }

    private static KnowledgeResult QueryServices(BusinessKnowledgeSnapshot snapshot, string? locationId, string? searchTerm)
    {
        var items = snapshot.Services.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(locationId))
            items = items.Where(i => i.LocationScope == "ALL" || (i.LocationIds != null && i.LocationIds.Contains(locationId)));

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.ToLowerInvariant();
            items = items.Where(i => i.Name.ToLowerInvariant().Contains(term) || (i.Description != null && i.Description.ToLowerInvariant().Contains(term)));
        }

        var resultList = items.Take(10).ToList();
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