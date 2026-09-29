using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexFlow.Infrastructure.Migrations;

public partial class DurableBackgroundLifecycle : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        foreach (var column in new[] { "ProcessingStartedAt", "LeaseUntil", "NextRetryAt" })
            migrationBuilder.AddColumn<DateTime>(column, "OutboxMessages", type: "timestamp with time zone", nullable: true);
        migrationBuilder.CreateTable("TenantDeletionJobs", columns: table => new
        {
            WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
            RequestedBy = table.Column<Guid>(type: "uuid", nullable: false),
            CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            Status = table.Column<int>(type: "integer", nullable: false),
            RetryCount = table.Column<int>(type: "integer", nullable: false),
            LeaseUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
            NextRetryAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
            Error = table.Column<string>(type: "text", nullable: true)
        }, constraints: table => table.PrimaryKey("PK_TenantDeletionJobs", x => x.WorkspaceId));
        migrationBuilder.CreateIndex("IX_TenantDeletionJobs_Status_NextRetryAt_LeaseUntil", "TenantDeletionJobs", new[] { "Status", "NextRetryAt", "LeaseUntil" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("TenantDeletionJobs");
        foreach (var column in new[] { "ProcessingStartedAt", "LeaseUntil", "NextRetryAt" })
            migrationBuilder.DropColumn(column, "OutboxMessages");
    }
}
