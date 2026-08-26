using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Common;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace NexFlow.Application.Features.SuperAdmin.Workspaces;

// 🔥 1. EL COMANDO QUE FALTABA
public record DeleteClientCommand(Guid WorkspaceId);

// 🔥 2. EL MANEJADOR
public class DeleteClientCommandHandler
{
    private readonly IWorkspaceRepository _workspaceRepository;
    private readonly ITenantCleanupService _cleanupService; // (El exterminador de Firestore)
    private readonly IUnitOfWork _unitOfWork;

    public DeleteClientCommandHandler(
        IWorkspaceRepository workspaceRepository,
        ITenantCleanupService cleanupService,
        IUnitOfWork unitOfWork)
    {
        _workspaceRepository = workspaceRepository;
        _cleanupService = cleanupService;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(DeleteClientCommand request, CancellationToken cancellationToken)
    {
        // Usamos la ruta SuperAdmin para encontrarlo
        var workspace = await _workspaceRepository.GetByIdForSuperAdminAsync(request.WorkspaceId, cancellationToken);
        if (workspace == null) return Result.Failure(new Error("Workspace.NotFound", "El negocio no existe."));

        // PASO 1: Borrar todo en FIRESTORE primero.
        // Lo hacemos antes de PostgreSQL para asegurar que si falla la red, 
        // no queden datos fantasma en la nube de Google.
        await _cleanupService.PurgeTenantDataAsync(request.WorkspaceId, cancellationToken);

        // PASO 2: Destrucción total en POSTGRESQL.
        // Llama al nuevo método que borra membresías, licencias, logs y reservas.
        await _workspaceRepository.DeleteNuclearAsync(workspace, cancellationToken);

        // PASO 3: Commit a la base de datos
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}