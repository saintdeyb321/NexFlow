using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace NexFlow.Infrastructure.Persistence.PostgreSQL.Context;

public class NexFlowDbContextFactory : IDesignTimeDbContextFactory<NexFlowDbContext>
{
    public NexFlowDbContext CreateDbContext(string[] args)
    {
        // Entity Framework ya se posiciona en la carpeta del startup-project
        var basePath = Directory.GetCurrentDirectory();

        var configuration = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .Build();

        var optionsBuilder = new DbContextOptionsBuilder<NexFlowDbContext>();

        var connectionString = configuration.GetConnectionString("DefaultConnection");
        optionsBuilder.UseNpgsql(connectionString);

        return new NexFlowDbContext(optionsBuilder.Options);
    }
}