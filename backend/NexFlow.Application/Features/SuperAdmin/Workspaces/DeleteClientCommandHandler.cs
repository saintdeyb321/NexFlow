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
        var workspace = await _workspaceRepository.GetByIdForSuperAdminAsync(request.WorkspaceId, cancellationToken);
        if (workspace == null) return Result.Failure(new Error("Workspace.NotFound", "El negocio no existe."));

        // 🔥 CORRECCIÓN (Fallos #10 y #50): PostgreSQL siempre manda.
        // PASO 1: Destrucción total en PostgreSQL y Commit.
        await _workspaceRepository.DeleteNuclearAsync(workspace, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // PASO 2: Borrar en FIRESTORE.
        // Lo hacemos DESPUÉS. Si la nube de Google falla temporalmente, 
        // el negocio ya fue destruido en SQL y el usuario no verá errores en la app.
        try
        {
            await _cleanupService.PurgeTenantDataAsync(request.WorkspaceId, cancellationToken);
        }
        catch (Exception)
        {
            // En un entorno productivo real aquí inyectarías ILogger para registrar:
            // "Alerta: El workspace se borró en SQL, pero falló la limpieza en Firestore."
            // Sin embargo, NO detenemos el Result.Success() porque el negocio ya no existe para el cliente.
        }

        return Result.Success();
    }
}