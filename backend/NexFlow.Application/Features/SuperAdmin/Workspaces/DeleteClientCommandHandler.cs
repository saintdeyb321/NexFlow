using Microsoft.Extensions.Logging;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Common;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace NexFlow.Application.Features.SuperAdmin.Workspaces;

public record DeleteClientCommand(Guid WorkspaceId);

public class DeleteClientCommandHandler
{
    private readonly IWorkspaceRepository _workspaceRepository;
    private readonly ITenantCleanupService _cleanupService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<DeleteClientCommandHandler> _logger; // 🔥 SPRINT 4.2: Logging explícito

    public DeleteClientCommandHandler(
        IWorkspaceRepository workspaceRepository,
        ITenantCleanupService cleanupService,
        IUnitOfWork unitOfWork,
        ILogger<DeleteClientCommandHandler> logger)
    {
        _workspaceRepository = workspaceRepository;
        _cleanupService = cleanupService;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result> Handle(DeleteClientCommand request, CancellationToken cancellationToken)
    {
        var workspace = await _workspaceRepository.GetByIdForSuperAdminAsync(request.WorkspaceId, cancellationToken);
        if (workspace == null) return Result.Failure(new Error("Workspace.NotFound", "El negocio no existe."));

        try
        {
            // 🔥 SPRINT 4.2: PASO 1 - Eliminar en FIRESTORE primero (Bloqueante).
            // Evitamos datos fantasma. Si Google Cloud falla, el proceso se aborta.
            await _cleanupService.PurgeTenantDataAsync(request.WorkspaceId, cancellationToken);

            // 🔥 SPRINT 4.2: PASO 2 - Destrucción total en PostgreSQL SOLO si Firestore tuvo éxito.
            await _workspaceRepository.DeleteNuclearAsync(workspace, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Eliminación completada (Completed) para el negocio: {WorkspaceId}", request.WorkspaceId);
            return Result.Success();
        }
        catch (Exception ex)
        {
            // 🔥 SPRINT 4.2: Registrar el fallo explícitamente (Failed) y abortar.
            _logger.LogError(ex, "Eliminación fallida (Failed) para el negocio: {WorkspaceId}. La transacción SQL fue abortada.", request.WorkspaceId);
            return Result.Failure(new Error("Workspace.DeletionFailed", $"Error crítico al eliminar el negocio: {ex.Message}"));
        }
    }
}