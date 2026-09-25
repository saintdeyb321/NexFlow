using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.Dashboard;

namespace NexFlow.API.Controllers.System;

[ApiController]
[Route("api/dashboard")]
[Authorize(Policy = "WorkspaceMember")]
public class DashboardController : ControllerBase
{
    private readonly IDashboardService _dashboardService;
    private readonly IWorkspaceContext _workspaceContext;

    public DashboardController(
        IDashboardService dashboardService,
        IWorkspaceContext workspaceContext)
    {
        _dashboardService = dashboardService;
        _workspaceContext = workspaceContext;
    }

    [HttpGet]
    public async Task<IActionResult> GetDashboardSummary(CancellationToken cancellationToken)
    {
        var workspaceId = _workspaceContext.CurrentWorkspaceId;

        var dashboardData = await _dashboardService.GetDashboardSummaryAsync(workspaceId, cancellationToken);

        return Ok(dashboardData);
    }
}