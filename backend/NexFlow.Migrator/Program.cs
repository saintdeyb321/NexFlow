using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Context;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Seeders;
using Npgsql;

const string connectionKey = "ConnectionStrings:DefaultConnection";
var stage = "configuration";
try
{
    // Runtime configuration only; no credential files or integration startup.
    var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
    var connectionString = configuration.GetConnectionString("DefaultConnection");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        Console.Error.WriteLine($"Missing or invalid configuration: {connectionKey}.");
        return 1;
    }
    try
    {
        var connection = new NpgsqlConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(connection.Host) || string.IsNullOrWhiteSpace(connection.Database)
            || string.IsNullOrWhiteSpace(connection.Username))
        {
            Console.Error.WriteLine($"Missing or invalid configuration: {connectionKey}.");
            return 1;
        }
    }
    catch (ArgumentException)
    {
        Console.Error.WriteLine($"Missing or invalid configuration: {connectionKey}.");
        return 1;
    }

    var services = new ServiceCollection();
    services.AddDbContext<NexFlowDbContext>(options => options.UseNpgsql(connectionString));
    await using var provider = services.BuildServiceProvider();
    await using var scope = provider.CreateAsyncScope();
    var context = scope.ServiceProvider.GetRequiredService<NexFlowDbContext>();

    stage = "migrations";
    await context.Database.MigrateAsync();
    stage = "system catalog seeding";
    await SystemCatalogSeeder.SeedCatalogAsync(context);
    Console.WriteLine("Migrations and system catalog seeding completed.");
    return 0;
}
catch
{
    // Provider exceptions may contain connection details or server data.
    Console.Error.WriteLine($"Migrator failed during {stage}; sensitive error details are omitted.");
    return 1;
}
