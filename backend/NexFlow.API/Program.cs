using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using NexFlow.API.Middleware;
using NexFlow.API.Security;
using NexFlow.API.Services;
using NexFlow.API.Services.BackgroundServices;
using NexFlow.Application.Abstractions;
using NexFlow.Application.DependencyInjection;
using NexFlow.Infrastructure.DependencyInjection;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Context;
using StackExchange.Redis;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// 1. Ensamblar Clean Architecture
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// 2. Servicios de Contexto Web
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<IWorkspaceContext, WorkspaceContext>();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IWebhookTaskQueue, WebhookTaskQueue>();
builder.Services.AddHostedService<WebhookProcessingBackgroundService>();

// 3. Configurar Firebase Authentication (JWT)
var firebaseProjectId = builder.Configuration["Firebase:ProjectId"];
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = $"https://securetoken.google.com/{firebaseProjectId}";
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = $"https://securetoken.google.com/{firebaseProjectId}",
            ValidateAudience = true,
            ValidAudience = firebaseProjectId,
            ValidateLifetime = true
        };
    });

// 4. Inyectar el Guardia de Seguridad
builder.Services.AddScoped<IAuthorizationHandler, SuperAdminHandler>();
builder.Services.AddScoped<IAuthorizationHandler, WorkspaceMemberHandler>();

// 5. Configurar las Políticas
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("SuperAdmin", policy => policy.Requirements.Add(new SuperAdminRequirement()));
    options.AddPolicy("WorkspaceMember", policy => policy.Requirements.Add(new WorkspaceMemberRequirement()));
});

// 6. RATE LIMITING GLOBAL ENTERPRISE
var rateLimitConfig = builder.Configuration.GetSection("RateLimiting");
var permitLimit = rateLimitConfig.GetValue<int>("PermitLimit", 100);
var windowMinutes = rateLimitConfig.GetValue<int>("WindowMinutes", 1);
var queueLimit = rateLimitConfig.GetValue<int>("QueueLimit", 2);

builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        if (context.Request.Path.StartsWithSegments("/api/webhooks/evolution"))
        {
            return RateLimitPartition.GetFixedWindowLimiter("evolution_webhook_limiter",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    AutoReplenishment = true,
                    PermitLimit = 2000,
                    Window = TimeSpan.FromMinutes(1)
                });
        }

        var partitionKey = context.User.Identity?.IsAuthenticated == true
            ? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "unknown_user"
            : context.Connection.RemoteIpAddress?.ToString() ?? "unknown_ip";

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = permitLimit,
                Window = TimeSpan.FromMinutes(windowMinutes),
                QueueLimit = queueLimit
            });
    });
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

// 7. CORS
var allowedOrigins = builder.Configuration["Cors:AllowedOrigins"]?.Split(',') ?? new[] { "http://localhost:5173" };
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .WithExposedHeaders("X-Correlation-ID");
    });
});

// 8. HEALTH CHECKS REALES DE INFRAESTRUCTURA
builder.Services.AddHealthChecks()
    .AddCheck<NexFlow.API.Services.RedisHealthCheck>( 
        "Redis",
        failureStatus: HealthStatus.Degraded,
        tags: new[] { "ready", "cache" })
    .AddCheck("Firestore", () =>
    {
        if (string.IsNullOrWhiteSpace(builder.Configuration["Firebase:ProjectId"]))
        {
            return HealthCheckResult.Unhealthy("Firebase:ProjectId no configurado.");
        }
        return HealthCheckResult.Healthy("Firestore configurado.");
    }, tags: new[] { "ready", "nosql" });

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// --- ADVERTENCIAS DE INICIO (STARTUP WARNINGS) ---
if (string.IsNullOrWhiteSpace(app.Configuration["Firebase:ProjectId"]))
{
    app.Logger.LogCritical("⚠️ ADVERTENCIA CRÍTICA: Firebase:ProjectId ausente. Firestore no funcionará.");
}

// --- PIPELINE DE MIDDLEWARES ---
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<GlobalExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    using var scope = app.Services.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<NexFlowDbContext>();
    await context.Database.MigrateAsync();
    await NexFlow.Infrastructure.Persistence.PostgreSQL.Seeders.SystemCatalogSeeder.SeedCatalogAsync(context);
}
else
{
    using var scope = app.Services.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<NexFlowDbContext>();
    await NexFlow.Infrastructure.Persistence.PostgreSQL.Seeders.SystemCatalogSeeder.SeedCatalogAsync(context);
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseCors("AllowFrontend");
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<UserIdentityMiddleware>();
app.UseAuthorization();

// Función formateadora de reporte JSON
static Task WriteHealthResponse(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json";
    var response = new
    {
        status = report.Status.ToString(),
        totalDurationMs = report.TotalDuration.TotalMilliseconds,
        checks = report.Entries.Select(e => new
        {
            component = e.Key,
            status = e.Value.Status.ToString(),
            description = e.Value.Description,
            durationMs = e.Value.Duration.TotalMilliseconds
        })
    };
    return context.Response.WriteAsJsonAsync(response);
}

// Liveness: El proceso está vivo
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = WriteHealthResponse
});

// Readiness: Los servicios y bases de datos están listos
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = WriteHealthResponse
});

// Endpoint general por compatibilidad
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = WriteHealthResponse
});

app.MapControllers();
app.Run();