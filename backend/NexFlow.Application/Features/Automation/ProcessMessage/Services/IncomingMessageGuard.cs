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
    private readonly IProcessedMessageRepository _processedMessageRepo;
    private readonly IEntitlementService _entitlementService;
    private readonly ILogger<IncomingMessageGuard> _logger;

    public IncomingMessageGuard(IInstanceResolver instanceResolver, IProcessedMessageRepository processedMessageRepo, IEntitlementService entitlementService, ILogger<IncomingMessageGuard> logger)
    {
        _instanceResolver = instanceResolver; _processedMessageRepo = processedMessageRepo; _entitlementService = entitlementService; _logger = logger;
    }

    private static string NormalizePhone(string phone)
    {
        if (string.IsNullOrWhiteSpace(phone) || phone.Contains("@g.us") || phone.Contains("status@broadcast")) return string.Empty;
        var clean = new string(phone.Split('@')[0].Where(char.IsDigit).ToArray());
        return (clean.Length >= 10 && clean.Length <= 15) ? "+" + clean : string.Empty;
    }

    public async Task<(bool IsValid, Guid WorkspaceId, string NormalizedPhone)> CheckMessageAsync(ProcessIncomingMessageCommand request, CancellationToken cancellationToken)
    {
        var resolvedId = await _instanceResolver.ResolveInstanceAsync(request.InstanceName, cancellationToken);
        if (resolvedId == null || resolvedId == Guid.Empty)
        {
            _logger.LogWarning("Incoming message rejected because the instance '{InstanceName}' could not be resolved.", request.InstanceName);
            return (false, Guid.Empty, string.Empty);
        }

        if (!await _processedMessageRepo.TryAcquireLockAsync(resolvedId.Value, request.MessageId, cancellationToken))
        {
            _logger.LogWarning("Incoming message {MessageId} for workspace {WorkspaceId} was rejected because it is already locked or processed.", request.MessageId, resolvedId.Value);
            return (false, Guid.Empty, string.Empty);
        }

        var normalizedPhone = NormalizePhone(request.CustomerPhone);
        if (string.IsNullOrEmpty(normalizedPhone))
        {
            _logger.LogWarning("Incoming message {MessageId} from instance '{InstanceName}' was rejected because the phone was invalid or empty.", request.MessageId, request.InstanceName);
            return (false, Guid.Empty, string.Empty);
        }

        if (!await _entitlementService.IsLicenseValidAsync(resolvedId.Value, cancellationToken))
        {
            _logger.LogWarning("Incoming message {MessageId} was rejected because the license for workspace {WorkspaceId} is not valid.", request.MessageId, resolvedId.Value);
            return (false, Guid.Empty, string.Empty);
        }

        return (true, resolvedId.Value, normalizedPhone);
    }
}