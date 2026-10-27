using Microsoft.Extensions.Logging;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.Business.Offerings;
using NexFlow.Application.Features.Business;
using System.Text;
using NexFlow.Application.Features.Automation.ProcessMessage.Services;

namespace NexFlow.Application.Features.Knowledge;

public sealed class KnowledgeService : IKnowledgeService
{
    private readonly IBusinessProfileRepository _profileRepo;
    private readonly ILocationRepository _locationRepo;
    private readonly IBusinessHoursRepository _hoursRepo;
    private readonly IFaqRepository _faqRepo;
    private readonly IOfferingService _offeringService;
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
        };
    }

    public Task<KnowledgeResult> QueryAsync(Guid workspaceId, BusinessKnowledgeSnapshot snapshot, KnowledgeQuery query, CancellationToken cancellationToken)
        => ConversationDiagnostics.MeasureAsync(_logger, "KnowledgeQuery", () => QueryCoreAsync(workspaceId, snapshot, query, cancellationToken), result => result.Status.ToString(), reason: query.Topic.ToString());

    private async Task<KnowledgeResult> QueryCoreAsync(Guid workspaceId, BusinessKnowledgeSnapshot snapshot, KnowledgeQuery query, CancellationToken cancellationToken)
    {
        try
        {
            if (snapshot.WorkspaceId != workspaceId) throw new InvalidOperationException("Knowledge workspace mismatch.");
            if (query.Limit is < 1 or > 5 || query.Offset is < 0 or > 500 || query.SearchTerm?.Length > 200)
                throw new ArgumentException("Invalid bounded knowledge query.");
            var locationId = query.LocationId;
            List<LocationDto>? locations = snapshot.CurrentQueryLocations?.ToList();
            if (query.Topic == KnowledgeTopic.Locations || !string.IsNullOrWhiteSpace(locationId))
            {
                locations ??= (await _locationRepo.GetLocationsAsync(workspaceId, cancellationToken)).ToList();
                if (!string.IsNullOrWhiteSpace(locationId) && !locations.Any(l => l.Id == locationId)) return Result(query, locationId);
            }

            string facts;
            switch (query.Topic)
            {
                case KnowledgeTopic.Locations:
                    var locationsPage = locations!.Where(l => locationId == null || l.Id == locationId)
                        .OrderBy(l => l.Id, StringComparer.Ordinal).Skip(query.Offset).Take(query.Limit + 1).ToArray();
                    return new KnowledgeResult { Source = query.Topic, LocationId = locationId,
                        Status = locationsPage.Length == 0 ? KnowledgeStatus.NotFound : KnowledgeStatus.Found,
                        Facts = QueryLocations(locationsPage.Take(query.Limit), locationId), HasMore = locationsPage.Length > query.Limit,
                        References = locationsPage.Take(query.Limit).Where(l => l.Id != null).Select(l => new KnowledgeLocationChoice(l.Id!, l.Name)).ToArray() };
                case KnowledgeTopic.BusinessHours:
                    return await QueryBusinessHoursAsync(workspaceId, query, locations, cancellationToken);
                case KnowledgeTopic.Faqs:
                    facts = QueryFaqs((await _faqRepo.GetFaqsAsync(workspaceId, cancellationToken)).ToList(), query.SearchTerm, query.Offset, query.Limit);
                    break;
                case KnowledgeTopic.Profile:
                    var profile = await _profileRepo.GetProfileAsync(workspaceId, cancellationToken);
                    return new KnowledgeResult { Source = query.Topic, Status = profile == null ? KnowledgeStatus.NotFound : KnowledgeStatus.Found,
                        Facts = QueryProfile(profile), Profile = profile == null ? null : new(profile.CommercialName, profile.Description,
                            profile.ContactEmail, profile.WhatsAppNumber, profile.TimeZone) };
                case KnowledgeTopic.Products:
                case KnowledgeTopic.Offerings: // Existing callers used Offerings for products.
                case KnowledgeTopic.Services:
                    return await QueryOfferingsAsync(workspaceId, query, cancellationToken);
                default:
                    return Result(query, locationId);
            }
            return Result(query, locationId, facts);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falla al consultar Knowledge {Topic} para el workspace {WorkspaceId}.", query.Topic, workspaceId);
            return new KnowledgeResult { Status = KnowledgeStatus.Unavailable, Source = query.Topic, LocationId = query.LocationId };
        }
    }

    private static KnowledgeResult Result(KnowledgeQuery query, string? locationId, string facts = "") => new()
    {
        Status = string.IsNullOrWhiteSpace(facts) ? KnowledgeStatus.NotFound : KnowledgeStatus.Found,
        Facts = facts,
        Source = query.Topic,
        LocationId = locationId
    };

    private static string QueryLocations(IEnumerable<LocationDto> locations, string? locationId)
    {
        var sb = new StringBuilder();
        foreach (var location in locations.Where(l => string.IsNullOrWhiteSpace(locationId) || l.Id == locationId))
        {
            sb.AppendLine($"- {location.Name} {(location.IsMain ? "(Sede Principal)" : "")}. Dirección: {location.Address}");
            if (!string.IsNullOrWhiteSpace(location.Reference)) sb.AppendLine($"  Referencia: {location.Reference}");
            if (!string.IsNullOrWhiteSpace(location.MapUrl)) sb.AppendLine($"  Mapa: {location.MapUrl}");
        }
        return sb.ToString().Trim();
    }

    private async Task<KnowledgeResult> QueryOfferingsAsync(Guid workspaceId, KnowledgeQuery query, CancellationToken ct)
    {
        var service = query.Topic == KnowledgeTopic.Services;
        IEnumerable<NexFlow.Application.Features.Shared.DTOs.BusinessOfferingDto> values = service
            ? await _offeringService.GetServicesAsync(workspaceId, query.LocationId, query.SearchTerm, ct)
            : await _offeringService.GetProductsAsync(workspaceId, query.LocationId, query.SearchTerm, ct);
        var page = values.Where(v => v.IsActive && v.Type == (service ? "SERVICE" : "PRODUCT"))
            .OrderBy(v => v.Id, StringComparer.Ordinal).Skip(query.Offset).Take(query.Limit + 1).ToArray();
        var selected = page.Take(query.Limit).ToArray();
        var sb = new StringBuilder();
        foreach (var item in selected)
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.AppendLine($"• *{item.Name}*\n  {NexFlow.Application.Features.Automation.Dialogue.Composition.OfferingResponseFormatter.Money(item.Currency, item.PriceMinorUnits)}");
            if (item is NexFlow.Application.Features.Services.DTOs.ServiceDto { DurationInMinutes: { } duration }) sb.AppendLine($"  Duración: {duration} min");
            if (!string.IsNullOrWhiteSpace(query.SearchTerm) && !string.IsNullOrWhiteSpace(item.Description)) sb.AppendLine($"  Detalle: {item.Description}");
        }
        return new KnowledgeResult { Source = query.Topic, LocationId = query.LocationId, Status = selected.Length == 0 ? KnowledgeStatus.NotFound : KnowledgeStatus.Found,
            Facts = sb.ToString().Trim(), HasMore = page.Length > query.Limit,
            Offerings = Array.AsReadOnly(selected.Select(NexFlow.Application.Features.Automation.Dialogue.Contracts.VerifiedOffering.Capture).ToArray()) };
    }

    private async Task<KnowledgeResult> QueryBusinessHoursAsync(Guid workspaceId, KnowledgeQuery query, List<LocationDto>? locations, CancellationToken ct)
    {
        var records = (await _hoursRepo.GetBusinessHoursAsync(workspaceId, query.LocationId, ct)).ToList();
        if (!string.IsNullOrWhiteSpace(query.LocationId))
        {
            var location = locations!.Single(l => l.Id == query.LocationId);
            return Result(query, location.Id, records.Count == 0
                ? $"No hay horarios de atención configurados para {location.Name}. Esto no indica que esté cerrada."
                : $"Horario de atención de {location.Name}:\n{BusinessHoursFormatter.Format(records)}");
        }
        if (records.Count > 0) return Result(query, null, $"Horario general de atención:\n{BusinessHoursFormatter.Format(records)}");

        // No calendar fan-out: read at most one additional schedule, or ask for a bounded choice.
        locations ??= (await _locationRepo.GetLocationsAsync(workspaceId, ct)).ToList();
        var choices = locations.Where(l => !string.IsNullOrWhiteSpace(l.Id)).OrderBy(l => l.Name).ThenBy(l => l.Id).Take(6).ToArray();
        if (choices.Length == 0) return Result(query, null, "No hay un horario general ni sedes configuradas para consultar. Puedes solicitar un asesor.");
        if (choices.Length == 1)
        {
            var location = choices[0];
            var schedule = (await _hoursRepo.GetBusinessHoursAsync(workspaceId, location.Id, ct)).ToList();
            return Result(query, location.Id, schedule.Count == 0
                ? $"No hay horarios de atención configurados para {location.Name}. Esto no indica que esté cerrada."
                : $"Horario de atención de {location.Name}:\n{BusinessHoursFormatter.Format(schedule)}");
        }
        var page = choices.Take(5).Select(l => new KnowledgeLocationChoice(l.Id!, l.Name)).ToArray();
        return new KnowledgeResult {
            Status = KnowledgeStatus.Found, Source = KnowledgeTopic.BusinessHours, LocationChoices = page,
            Facts = "No hay un horario general configurado. ¿De qué sede deseas consultar los horarios?\n"
                + string.Join("\n", page.Select((l, index) => $"{index + 1}. {l.Name}"))
                + (choices.Length > 5 ? "\nTambién puedes indicar el nombre de otra sede." : "")
        };
    }

    private static string QueryFaqs(IEnumerable<FaqDto> faqs, string? searchTerm, int offset, int limit)
    {
        var active = faqs.Where(f => f.IsActive && !string.IsNullOrWhiteSpace(f.Answer));
        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = Normalize(searchTerm);
            active = active.Where(f => Normalize(f.Question).Contains(term) || Normalize(f.Answer).Contains(term));
        }
        return string.Join("\n\n", active.OrderBy(f => f.Id, StringComparer.Ordinal).Skip(offset).Take(limit).Select(f => $"P: {f.Question}\nR: {f.Answer}"));
    }

    private static string QueryProfile(BusinessProfileDto? profile)
    {
        if (profile == null) return string.Empty;
        var facts = new List<string>();
        if (!string.IsNullOrWhiteSpace(profile.CommercialName)) facts.Add($"Negocio: {profile.CommercialName}");
        if (!string.IsNullOrWhiteSpace(profile.Description)) facts.Add(profile.Description);
        if (!string.IsNullOrWhiteSpace(profile.ContactEmail)) facts.Add($"Correo: {profile.ContactEmail}");
        if (!string.IsNullOrWhiteSpace(profile.WhatsAppNumber)) facts.Add($"WhatsApp: {profile.WhatsAppNumber}");
        return string.Join("\n", facts);
    }

    private static string Normalize(string value) => new string(value.Normalize(System.Text.NormalizationForm.FormD)
        .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
        .ToArray()).ToLowerInvariant().Trim().Trim('¿', '?', '¡', '!').Trim();
}

