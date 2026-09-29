using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexFlow.Infrastructure.Migrations;

public partial class ReservationInboundIdempotency : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("SourceMessageId", "Reservations", type: "text", nullable: true);
        migrationBuilder.CreateIndex("IX_Reservations_WorkspaceId_SourceMessageId", "Reservations",
            new[] { "WorkspaceId", "SourceMessageId" }, unique: true, filter: "\"SourceMessageId\" IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("IX_Reservations_WorkspaceId_SourceMessageId", "Reservations");
        migrationBuilder.DropColumn("SourceMessageId", "Reservations");
    }
}
