using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TrackWorkspaceWhatsAppConnections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WhatsAppConnections",
                columns: table => new
                {
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    PersistenceVersion = table.Column<Guid>(type: "uuid", nullable: false),
                    IsLinked = table.Column<bool>(type: "boolean", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    QrBase64 = table.Column<string>(type: "text", nullable: true),
                    QrExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ObservedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: true),
                    OperationUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LogoutPending = table.Column<bool>(type: "boolean", nullable: false),
                    LastLogoutAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LoggedOutOwner = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppConnections", x => x.WorkspaceId);
                    table.ForeignKey(
                        name: "FK_WhatsAppConnections_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Reserve local identities only. Preserve every existing provider name and session.
            migrationBuilder.Sql("""
                UPDATE "Workspaces"
                SET "EvolutionInstanceName" = 'nexflow' || replace("Id"::text, '-', '')
                WHERE "EvolutionInstanceName" IS NULL OR btrim("EvolutionInstanceName") = '';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Assigned instance identities remain stable; rollback must not orphan an external session.
            migrationBuilder.DropTable(
                name: "WhatsAppConnections");
        }
    }
}
