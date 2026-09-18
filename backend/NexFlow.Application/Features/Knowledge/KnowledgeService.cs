using System.Text;
using Microsoft.Extensions.Logging;
using NexFlow.Application.Abstractions;

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
        _logger.LogInformation("Obteniendo Snapshot de Conocimiento para Workspace {WorkspaceId}", workspaceId);

        var profileTask = _profileRepo.GetProfileAsync(workspaceId, cancellationToken);
        var locationsTask = _locationRepo.GetLocationsAsync(workspaceId, cancellationToken);
        var hoursTask = _hoursRepo.GetBusinessHoursAsync(workspaceId, null, cancellationToken);
        var faqsTask = _faqRepo.GetFaqsAsync(workspaceId, cancellationToken);
        var catalogTask = _catalogRepo.GetActiveItemsAsync(workspaceId, cancellationToken);

        await Task.WhenAll(profileTask, locationsTask, hoursTask, faqsTask, catalogTask);

        return new BusinessKnowledgeSnapshot
        {
            WorkspaceId = workspaceId,
            Profile = profileTask.Result,
            Locations = locationsTask.Result?.ToList() ?? new(),
            Hours = hoursTask.Result?.ToList() ?? new(),
            Faqs = faqsTask.Result?.ToList() ?? new(),
            CatalogItems = catalogTask.Result?.ToList() ?? new()
        };
    }

    public KnowledgeResult Query(BusinessKnowledgeSnapshot snapshot, KnowledgeQuery query)
    {
        return query.Topic switch
        {
            KnowledgeTopic.Locations => QueryLocations(snapshot, query.LocationId),
            KnowledgeTopic.Offerings => QueryOfferings(snapshot, query.LocationId, query.SearchTerm),
            KnowledgeTopic.BusinessHours => QueryHours(snapshot),
            KnowledgeTopic.Faqs => QueryFaqs(snapshot, query.SearchTerm),
            KnowledgeTopic.Profile => QueryProfile(snapshot),
            _ => new KnowledgeResult { Found = false, Source = query.Topic }
        };
    }

    private static KnowledgeResult QueryLocations(BusinessKnowledgeSnapshot snapshot, string? locationId)
    {
        var locs = string.IsNullOrWhiteSpace(locationId)
            ? snapshot.Locations
            : snapshot.Locations.Where(l => l.Id == locationId).ToList();

        if (!locs.Any()) return new KnowledgeResult { Found = false, Source = KnowledgeTopic.Locations };

        var sb = new StringBuilder();
        foreach (var l in locs)
        {
            sb.AppendLine($"- {l.Name} {(l.IsMain ? "(Sede Principal)" : "")}");
            sb.AppendLine($"  Dirección: {l.Address}");
            if (!string.IsNullOrWhiteSpace(l.Reference)) sb.AppendLine($"  Referencia: {l.Reference}");
            if (!string.IsNullOrWhiteSpace(l.MapUrl)) sb.AppendLine($"  Mapa: {l.MapUrl}");
            sb.AppendLine();
        }

        return new KnowledgeResult { Found = true, Facts = sb.ToString(), Source = KnowledgeTopic.Locations, LocationId = locationId };
    }

    private static KnowledgeResult QueryOfferings(BusinessKnowledgeSnapshot snapshot, string? locationId, string? searchTerm)
    {
        var items = snapshot.CatalogItems.AsEnumerable();

        // 1. Filtrar por Sede (Location Isolation)
        if (!string.IsNullOrWhiteSpace(locationId))
        {
            items = items.Where(i =>
                i.LocationScope.Equals("ALL", StringComparison.OrdinalIgnoreCase) ||
                (i.LocationIds != null && i.LocationIds.Contains(locationId)));
        }

        // 2. Filtrar por término de búsqueda (Fuzzy)
        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.ToLowerInvariant();
            items = items.Where(i =>
                i.Name.ToLowerInvariant().Contains(term) ||
                (i.Description != null && i.Description.ToLowerInvariant().Contains(term)));
        }

        var resultList = items.ToList();
        if (!resultList.Any()) return new KnowledgeResult { Found = false, Source = KnowledgeTopic.Offerings, LocationId = locationId };

        var sb = new StringBuilder();
        foreach (var item in resultList.Take(10)) // Limitamos a 10 para no saturar contextos
        {
            sb.AppendLine($"- {item.Name} ({item.Type})");
            if (!string.IsNullOrWhiteSpace(item.Description)) sb.AppendLine($"  Descripción: {item.Description}");
            sb.AppendLine($"  Precio: {item.PriceMinorUnits / 100.0m} {item.Currency}");
            if (item.DurationInMinutes.HasValue) sb.AppendLine($"  Duración: {item.DurationInMinutes} minutos");
            sb.AppendLine();
        }

        return new KnowledgeResult { Found = true, Facts = sb.ToString(), Source = KnowledgeTopic.Offerings, LocationId = locationId };
    }

    private static KnowledgeResult QueryHours(BusinessKnowledgeSnapshot snapshot)
    {
        if (!snapshot.Hours.Any()) return new KnowledgeResult { Found = false, Source = KnowledgeTopic.BusinessHours };

        var days = new[] { "Domingo", "Lunes", "Martes", "Miércoles", "Jueves", "Viernes", "Sábado" };
        var sb = new StringBuilder();

        foreach (var h in snapshot.Hours.OrderBy(x => x.DayOfWeek))
        {
            var dayName = h.DayOfWeek >= 0 && h.DayOfWeek <= 6 ? days[h.DayOfWeek] : "Día";
            if (h.IsClosed) sb.AppendLine($"- {dayName}: Cerrado");
            else sb.AppendLine($"- {dayName}: {h.OpenTime} a {h.CloseTime}");
        }

        return new KnowledgeResult { Found = true, Facts = sb.ToString(), Source = KnowledgeTopic.BusinessHours };
    }

    private static KnowledgeResult QueryFaqs(BusinessKnowledgeSnapshot snapshot, string? searchTerm)
    {
        var faqs = snapshot.Faqs.Where(f => f.IsActive);

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.ToLowerInvariant();
            faqs = faqs.Where(f => f.Question.ToLowerInvariant().Contains(term) || f.Answer.ToLowerInvariant().Contains(term));
        }

        var resultList = faqs.ToList();
        if (!resultList.Any()) return new KnowledgeResult { Found = false, Source = KnowledgeTopic.Faqs };

        var sb = new StringBuilder();
        foreach (var f in resultList.Take(5)) // Solo las mejores coincidencias
        {
            sb.AppendLine($"P: {f.Question}");
            sb.AppendLine($"R: {f.Answer}");
            sb.AppendLine();
        }

        return new KnowledgeResult { Found = true, Facts = sb.ToString(), Source = KnowledgeTopic.Faqs };
    }

    private static KnowledgeResult QueryProfile(BusinessKnowledgeSnapshot snapshot)
    {
        if (snapshot.Profile == null) return new KnowledgeResult { Found = false, Source = KnowledgeTopic.Profile };
        return new KnowledgeResult { Found = true, Facts = snapshot.Profile.Description ?? "Sin descripción", Source = KnowledgeTopic.Profile };
    }
}