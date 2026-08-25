using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using NexFlow.API.Middleware;
using NexFlow.API.Security;
using NexFlow.API.Services;
using NexFlow.Application.Abstractions;
using NexFlow.Application.DependencyInjection;
using NexFlow.Infrastructure.DependencyInjection;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Context;
using System.Threading.RateLimiting;
using System.Security.Claims;
using System;

var builder = WebApplication.CreateBuilder(args);

// 1. Ensamblar Clean Architecture
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// 2. Servicios de Contexto Web
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<IWorkspaceContext, WorkspaceContext>();

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

// 6. RATE LIMITING GLOBAL ENTERPRISE (Tenant & Webhook Aware)
var rateLimitConfig = builder.Configuration.GetSection("RateLimiting");
var permitLimit = rateLimitConfig.GetValue<int>("PermitLimit", 100);
var windowMinutes = rateLimitConfig.GetValue<int>("WindowMinutes", 1);
var queueLimit = rateLimitConfig.GetValue<int>("QueueLimit", 2);

builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        // 🔥 CORRECCIÓN BLINDAJE 1: Identificamos el Webhook por su ruta oficial, no por un header falsificable.
        // La validación de la contraseña ocurre después, dentro del controlador.
        if (context.Request.Path.StartsWithSegments("/api/webhooks/evolution"))
        {
            return RateLimitPartition.GetFixedWindowLimiter("evolution_webhook_limiter",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    AutoReplenishment = true,
                    PermitLimit = 2000, // Alto límite para soportar la ráfaga de mensajes de WhatsApp
                    Window = TimeSpan.FromMinutes(1)
                });
        }

        // BLINDAJE 2: Tráfico normal (Frontend, Postman, etc.)
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
        policy.WithOrigins("http://localhost:5173")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .WithExposedHeaders("X-Correlation-ID");
    });
});

// 8. HEALTH CHECKS (V2.13: Producción y Observabilidad)
var redisConnectionString = builder.Configuration.GetConnectionString("RedisConnection") ?? "localhost:6379";

builder.Services.AddHealthChecks()
    // Verifica que PostgreSQL responda
    .AddDbContextCheck<NexFlowDbContext>(name: "PostgreSQL", tags: new[] { "db", "data" })
    // Verifica que Redis esté vivo
    .AddRedis(redisConnectionString, name: "Redis", tags: new[] { "cache", "locks" });
builder.Services.AddControllers();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// --- PIPELINE DE MIDDLEWARES (El orden es CRÍTICO) ---
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<GlobalExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("AllowFrontend");
app.UseRateLimiter();

app.UseAuthentication();
app.UseMiddleware<UserIdentityMiddleware>();
app.UseAuthorization();

// Exponer el endpoint de vida para Docker/Nginx
// Exponer el endpoint de vida para Docker/Nginx
app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var response = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new { component = e.Key, status = e.Value.Status.ToString(), description = e.Value.Description }),
            duration = report.TotalDuration
        };
        await context.Response.WriteAsJsonAsync(response);
    }
});

app.MapControllers();

// Asegurar que la BD esté migrada y el catálogo inicializado
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<NexFlowDbContext>();
    await context.Database.MigrateAsync();
    await NexFlow.Infrastructure.Persistence.PostgreSQL.Seeders.SystemCatalogSeeder.SeedCatalogAsync(context);
}

app.Run();