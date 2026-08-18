namespace NexFlow.Domain.Enums;

public enum UserStatus { Active, Inactive, Suspended }
public enum WorkspaceStatus { Active, Suspended, Archived }
public enum MembershipRole { Owner, Admin, Member }
public enum LicenseStatus { Pending, Active, Suspended, Expired, Cancelled }
public enum LicenseType { Template, Custom }
public enum ModuleStatus { Active, Inactive }
public enum TemplateStatus { Active, Inactive }

public enum AuditAction
{
    LicenseCreated,
    LicenseExtended,
    LicenseRenewed,
    LicenseSuspended,
    LicenseReactivated,
    LicenseCancelled,
    TemplateCreated,
    TemplateUpdated,
    TemplateChanged,
    TemplateActivated,
    TemplateDeactivated,
    ModuleAssigned,
    ModuleRemoved,
    MemberAdded,
    MemberRemoved,
    MemberRoleChanged,
    WorkspaceCreated,
    WorkspaceSuspended,
    WorkspaceActivated,
    UserCreated,
    UserSuspended
}