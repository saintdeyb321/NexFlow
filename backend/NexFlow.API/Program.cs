using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using NexFlow.API.Middleware;
using NexFlow.API.Configuration;
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

// Validate before registering infrastructure, creating SDK clients or workers.
if (builder.Environment.IsProduction()) RuntimeConfiguration.ValidateProduction(builder.Configuration);
var allowedOrigins = RuntimeConfiguration.GetAllowedOrigins(builder.Configuration, builder.Environment.IsProduction());
var knownNetworks = RuntimeConfiguration.GetKnownNetworks(builder.Configuration, builder.Environment.IsProduction());
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    if (knownNetworks.Length > 0)
    {
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        foreach (var network in knownNetworks) options.KnownIPNetworks.Add(network);
    }
});
builder.Services.AddHttpsRedirection(options => options.HttpsPort = 443);

// 1. Ensamblar Clean Architecture
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// 2. Servicios de Contexto Web
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<IWorkspaceContext, WorkspaceContext>();
builder.Services.AddMemoryCache();

builder.Services.AddSingleton<IBackgroundTaskQueue, BackgroundTaskQueue>();
builder.Services.AddHostedService<GenericBackgroundWorker>();

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

            var instanceName = context.Request.Query["instance"].FirstOrDefault() ?? context.Connection.RemoteIpAddress?.ToString() ?? "global_evolution";

            return RateLimitPartition.GetFixedWindowLimiter($"evolution_{instanceName}",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    AutoReplenishment = true,
                    PermitLimit = 1500,
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
    .AddCheck<PostgresHealthCheck>("PostgreSQL", failureStatus: HealthStatus.Unhealthy, tags: new[] { "ready", "database" }, timeout: TimeSpan.FromSeconds(6))
    .AddCheck<FirestoreHealthCheck>("Firestore", failureStatus: HealthStatus.Unhealthy, tags: new[] { "ready", "nosql" }, timeout: TimeSpan.FromSeconds(6));

builder.Services.AddScoped<TenantCapabilityFilter>();
builder.Services.AddControllers(options => { options.Filters.AddService<TenantCapabilityFilter>(); options.Filters.Add<ApiErrorResultFilter>(); })
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseForwardedHeaders();

// --- ADVERTENCIAS DE INICIO (STARTUP WARNINGS) ---
if (string.IsNullOrWhiteSpace(app.Configuration["Firebase:ProjectId"]))
{
    app.Logger.LogCritical("⚠️ ADVERTENCIA CRÍTICA: Firebase:ProjectId ausente. Firestore no funcionará.");
}

// --- PIPELINE DE MIDDLEWARES ---
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseStatusCodePages(async statusContext =>
{
    var http = statusContext.HttpContext;
    await http.Response.WriteAsJsonAsync(new { code = ApiErrorResultFilter.Code(http.Response.StatusCode), message = "La operación no está disponible.", correlationId = http.Items["CorrelationId"]?.ToString() ?? http.TraceIdentifier });
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    using var scope = app.Services.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<NexFlowDbContext>();
    await context.Database.MigrateAsync();
    await NexFlow.Infrastructure.Persistence.PostgreSQL.Seeders.SystemCatalogSeeder.SeedCatalogAsync(context);
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseCors("AllowFrontend");

app.UseAuthentication();
app.UseMiddleware<UserIdentityMiddleware>();
app.UseMiddleware<NexFlow.API.Middleware.TenantIsolationMiddleware>();
app.UseAuthorization();
app.UseRateLimiter();

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

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = WriteHealthResponse
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = WriteHealthResponse
});

app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = WriteHealthResponse
});

app.MapControllers();
app.Run();
