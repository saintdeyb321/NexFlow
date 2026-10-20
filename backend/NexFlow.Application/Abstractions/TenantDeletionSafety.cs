using NexFlow.Application.Common;

namespace NexFlow.Application.Abstractions;

// RC-3 release restriction. No configuration/admin override: enabling nuclear
// deletion requires a reviewed code release after remote-write and cleanup proof.
public static class TenantDeletionSafety
{
    public static bool IsEnabled => false;
    public static Error Unavailable => new("Workspace.DeletionUnavailable",
        "La eliminación definitiva está temporalmente deshabilitada mientras se certifica la limpieza segura. Puedes suspender el negocio.");
}
