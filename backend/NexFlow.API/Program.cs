using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting; // <-- NUEVO
using System.Threading.RateLimiting;     // <-- NUEVO
using NexFlow.Application.DependencyInjection;
using NexFlow.Infrastructure.DependencyInjection;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.API.Services;
using NexFlow.API.Security;
using NexFlow.API.Middleware;

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

// --- NUEVO: RATE LIMITING GLOBAL ---
builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            // Agrupamos por IP. Si no hay IP, agrupamos por Host.
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? context.Request.Headers.Host.ToString(),
            factory: partition => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 100, // Máximo 100 peticiones...
                Window = TimeSpan.FromMinutes(1), // ...por minuto, por IP.
                QueueLimit = 2
            }));
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});
// ------------------------------------

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseMiddleware<GlobalExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// --- NUEVO: ACTIVAR EL ESCUDO DE RATE LIMITING ---
app.UseRateLimiter();
// -------------------------------------------------

app.UseAuthentication();
app.UseMiddleware<UserIdentityMiddleware>();
app.UseAuthorization();
app.MapControllers();
app.Run();