namespace NexFlow.Domain.Enums;

public enum AuditAction
{
    // Acciones de Workspace y Usuario
    WorkspaceCreated,
    WorkspaceSuspended,
    UserLinked,
    UserSuspended,
    MemberAdded,

    // Acciones de Licenciamiento
    LicenseCreated, // <-- ¡Este era el que faltaba y causaba error!
    LicenseActivated,
    LicenseRenewed,
    LicenseExtended,
    LicenseSuspended,
    LicenseReactivated,
    ModuleAssigned,
    ModuleRemoved,

    // Acciones de Configuración y Negocio
    ConfigurationUpdated,
    ReservationCreated,
    ReservationCancelled,
    ReservationRescheduled
}