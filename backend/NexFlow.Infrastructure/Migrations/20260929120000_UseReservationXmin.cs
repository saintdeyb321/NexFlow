using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexFlow.Infrastructure.Migrations;

public partial class UseReservationXmin : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "RowVersion", table: "Reservations");

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<byte[]>(name: "RowVersion", table: "Reservations",
            type: "bytea", rowVersion: true, nullable: false, defaultValue: Array.Empty<byte>());
}
