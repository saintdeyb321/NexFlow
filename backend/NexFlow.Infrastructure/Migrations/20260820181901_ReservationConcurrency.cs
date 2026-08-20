using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReservationConcurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "Reservations",
                type: "text",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_WorkspaceId_LocationId_ServiceId_StartTime",
                table: "Reservations",
                columns: new[] { "WorkspaceId", "LocationId", "ServiceId", "StartTime" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Reservations_WorkspaceId_LocationId_ServiceId_StartTime",
                table: "Reservations");

            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "Reservations",
                type: "integer",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");
        }
    }
}
