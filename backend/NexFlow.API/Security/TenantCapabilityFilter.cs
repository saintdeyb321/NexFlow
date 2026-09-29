using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using NexFlow.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Context;

namespace NexFlow.API.Security;

public sealed class TenantCapabilityFilter(IEntitlementService entitlements, IWorkspaceContext workspace, IServiceScopeFactory scopes,
    ICurrentUser user, ILogger<TenantCapabilityFilter> logger) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (context.ActionDescriptor is not ControllerActionDescriptor action) { await next(); return; }
        var read = HttpMethods.IsGet(context.HttpContext.Request.Method);
        var name = action.ActionName;
        if (action.ControllerName == "Clients" && name != "DeleteClient")
        {
            var target = context.ActionArguments.Values.Select(v => v?.GetType().GetProperty("WorkspaceId")?.GetValue(v)).OfType<Guid>().FirstOrDefault();
            if (target != Guid.Empty)
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<NexFlowDbContext>();
                await using var lifecycle = await db.Database.BeginTransactionAsync(context.HttpContext.RequestAborted);
                await TenantLifecycleLock.AcquireAsync(db, target, false, context.HttpContext.RequestAborted);
                var tenant = await db.Workspaces.AsNoTracking().SingleOrDefaultAsync(w => w.Id == target, context.HttpContext.RequestAborted)
                    ?? throw new KeyNotFoundException("Workspace not found.");
                if (tenant.Status == NexFlow.Domain.Enums.WorkspaceStatus.Deleting) throw new NexFlow.Domain.Exceptions.ConcurrencyException("Workspace is being deleted.");
                logger.LogWarning("SuperAdmin operation: UserId {UserId}, WorkspaceId {WorkspaceId}, Action {Action}", user.UserId, target, name);
                await next();
                return;
            }
        }
        var module = action.ControllerName switch
        {
            "Reservations" => "RESERVATIONS", "Requests" => "REQUESTS", "Orders" => "ORDERS",
            "Services" => "SERVICES", "Catalog" => "CATALOG", "Conversations" or "Dashboard" => "CONVERSATIONS",
            "Business" => name.Contains("Hours") ? "BUSINESS_HOURS" : name.Contains("Location") ? "LOCATIONS"
                : name.Contains("Faq") ? "FAQ" : name.Contains("WhatsApp") ? "CONVERSATIONS" : "BUSINESS_PROFILE",
            _ => null
        };
        if (module == null) { await next(); return; }
        if (action.ControllerName == "Catalog")
        {
            var scope = context.HttpContext.Request.Query["scope"].FirstOrDefault()
                ?? context.ActionArguments.Values.OfType<NexFlow.Application.Features.Shared.DTOs.BusinessCategoryDto>().FirstOrDefault()?.Scope
                ?? context.ActionArguments.Values.OfType<Controllers.Business.GenerateArtifactRequest>().FirstOrDefault()?.Scope;
            if (name == "DeleteCategory" && context.ActionArguments.TryGetValue("categoryId", out var categoryId))
                scope = (await context.HttpContext.RequestServices.GetRequiredService<ICatalogRepository>().GetCategoryByIdAsync(workspace.CurrentWorkspaceId, (string)categoryId!, context.HttpContext.RequestAborted))?.Scope;
            scope = scope?.Trim().ToUpperInvariant();
            if (string.Equals(scope, "SERVICE", StringComparison.OrdinalIgnoreCase)) module = "SERVICES";
            if (name == "GetCategories" && (string.IsNullOrWhiteSpace(scope) || string.Equals(scope, "SHARED", StringComparison.OrdinalIgnoreCase)))
            {
                var ct = context.HttpContext.RequestAborted;
                if (await entitlements.HasCapabilityAccessAsync(workspace.CurrentWorkspaceId, "CATALOG", "READ", ct)
                    || await entitlements.HasCapabilityAccessAsync(workspace.CurrentWorkspaceId, "SERVICES", "READ", ct)) { await next(); return; }
            }
        }
        var capability = name switch
        {
            "GetAvailability" => "CHECK_AVAILABILITY", "UpdateReservationStatus" => "COMPLETE", "CancelReservation" => "CANCEL",
            "AssignRequest" => "ASSIGN", "UpdateStatus" => "UPDATE_STATUS", "SendManualMessage" => "SEND_MESSAGE",
            "TakeOverConversation" => "TAKEOVER", "ReleaseConversation" => "RELEASE",
            "GenerateArtifact" => "GENERATE", "ConnectWhatsApp" or "DisconnectWhatsApp" => "CONFIGURE",
            _ => read ? "READ" : HttpMethods.IsDelete(context.HttpContext.Request.Method) ? "DELETE"
                : HttpMethods.IsPost(context.HttpContext.Request.Method) ? "CREATE" : "UPDATE"
        };
        if (!await entitlements.HasCapabilityAccessAsync(workspace.CurrentWorkspaceId, module, capability, context.HttpContext.RequestAborted))
        {
            context.Result = new ObjectResult(new { code = "Security.CapabilityDenied", message = "El módulo o la capacidad no están disponibles para este usuario.", correlationId = context.HttpContext.TraceIdentifier }) { StatusCode = 403 };
            return;
        }
        await next();
    }
}
