using System;

namespace NexFlow.Application.Features.SuperAdmin.Workspaces;

public class WorkspaceSummaryDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Status { get; set; }
    public string OwnerEmail { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}