using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEvolutionInstanceName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EvolutionInstanceName",
                table: "Workspaces",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Workspaces_EvolutionInstanceName",
                table: "Workspaces",
                column: "EvolutionInstanceName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Workspaces_EvolutionInstanceName",
                table: "Workspaces");

            migrationBuilder.DropColumn(
                name: "EvolutionInstanceName",
                table: "Workspaces");
        }
    }
}
