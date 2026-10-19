using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CoordinateReservationResourcesAndReuseCancelledSlots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Reservations_WorkspaceId_LocationId_ServiceId_StartTime",
                table: "Reservations");

            migrationBuilder.CreateTable(
                name: "ReservationResourceStates",
                columns: table => new
                {
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ResourceId = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    PendingOperationId = table.Column<Guid>(type: "uuid", nullable: true),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: true),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReservationResourceStates", x => new { x.WorkspaceId, x.Kind, x.ResourceId });
                    table.ForeignKey(
                        name: "FK_ReservationResourceStates_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_WorkspaceId_LocationId_ServiceId_StartTime",
                table: "Reservations",
                columns: new[] { "WorkspaceId", "LocationId", "ServiceId", "StartTime" },
                unique: true,
                filter: "\"Status\" <> 'Cancelled'");

            migrationBuilder.CreateIndex(
                name: "IX_ReservationResourceStates_NextAttemptAt",
                table: "ReservationResourceStates",
                column: "NextAttemptAt",
                filter: "\"PendingOperationId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Run under the migration transaction. Never discard a durable fence
            // or rewrite reservation history to make an old schema fit.
            migrationBuilder.Sql("""
                LOCK TABLE "Reservations", "ReservationResourceStates" IN ACCESS EXCLUSIVE MODE;
                DO $guard$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "ReservationResourceStates") THEN
                        RAISE EXCEPTION 'Rollback refused: durable reservation resource epochs exist. Keep the compatible schema.' USING ERRCODE = '55000';
                    END IF;
                    IF EXISTS (
                        SELECT 1 FROM "Reservations"
                        GROUP BY "WorkspaceId", "LocationId", "ServiceId", "StartTime" HAVING COUNT(*) > 1
                    ) THEN
                        RAISE EXCEPTION 'Rollback refused: reused reservation slots are incompatible with the old unique index.' USING ERRCODE = '55000';
                    END IF;
                END
                $guard$;
                """);
            migrationBuilder.DropTable(
                name: "ReservationResourceStates");

            migrationBuilder.DropIndex(
                name: "IX_Reservations_WorkspaceId_LocationId_ServiceId_StartTime",
                table: "Reservations");

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_WorkspaceId_LocationId_ServiceId_StartTime",
                table: "Reservations",
                columns: new[] { "WorkspaceId", "LocationId", "ServiceId", "StartTime" },
                unique: true);
        }
    }
}
