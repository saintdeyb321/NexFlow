using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Repositories;

namespace NexFlow.Application.Features.Automation.ProcessMessage.Services;

public interface IIncomingMessageGuard
{
    Task<(bool IsValid, Guid WorkspaceId, string NormalizedPhone)> CheckMessageAsync(ProcessIncomingMessageCommand request, CancellationToken cancellationToken);
}

public class IncomingMessageGuard : IIncomingMessageGuard
{
    private readonly IInstanceResolver _instanceResolver;
    private readonly IEntitlementService _entitlementService;
    private readonly ILogger<IncomingMessageGuard> _logger;

    public IncomingMessageGuard(IInstanceResolver instanceResolver, IEntitlementService entitlementService, ILogger<IncomingMessageGuard> logger)
    {
        _instanceResolver = instanceResolver;
        _entitlementService = entitlementService;
        _logger = logger;
    }

    public static string NormalizePhone(string phone)
    {
        if (string.IsNullOrWhiteSpace(phone) || phone.Contains("@g.us") || phone.Contains("status@broadcast")) return string.Empty;
        var clean = new string(phone.Split('@')[0].Where(char.IsDigit).ToArray());
        return (clean.Length >= 10 && clean.Length <= 15) ? "+" + clean : string.Empty;
    }

    public async Task<(bool IsValid, Guid WorkspaceId, string NormalizedPhone)> CheckMessageAsync(ProcessIncomingMessageCommand request, CancellationToken cancellationToken)
    {
        var normalizedPhone = NormalizePhone(request.CustomerPhone);
        if (string.IsNullOrEmpty(normalizedPhone))
        {
            _logger.LogWarning("Incoming message {MessageId} rejected: phone invalid or empty.", request.MessageId);
            throw new InvalidOperationException("Inbound phone or instance is invalid.");
        }

        var resolvedId = await _instanceResolver.ResolveInstanceAsync(request.InstanceName, cancellationToken);
        if (resolvedId == null || resolvedId == Guid.Empty)
        {
            _logger.LogWarning("Incoming message rejected: instance '{InstanceName}' not resolved.", request.InstanceName);
            throw new InvalidOperationException("Inbound phone or instance is invalid.");
        }

        if (request.WorkspaceId.HasValue && request.WorkspaceId != resolvedId)
            throw new InvalidOperationException("Inbound workspace does not match its instance.");

        if (!await _entitlementService.IsLicenseValidAsync(resolvedId.Value, cancellationToken))
        {
            _logger.LogWarning("Incoming message {MessageId} rejected: license for workspace {WorkspaceId} invalid.", request.MessageId, resolvedId.Value);
            return (false, resolvedId.Value, normalizedPhone);
        }

        // 🔥 SPRINT 02: Ya no bloqueamos ni guardamos en la vieja tabla ProcessedMessages 
        // porque InboundMessageRepository ya gestiona el bloqueo a nivel de transacción PostgreSQL (FOR UPDATE SKIP LOCKED).

        return (true, resolvedId.Value, normalizedPhone);
    }
}