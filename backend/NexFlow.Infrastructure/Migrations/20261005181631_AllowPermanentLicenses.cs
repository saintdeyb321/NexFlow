using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AllowPermanentLicenses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTime>(
                name: "ValidTo",
                table: "Licenses",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // PostgreSQL rejects this rollback if permanent licenses still exist,
            // preserving their NULL expiration instead of inventing a date.
            migrationBuilder.Sql("ALTER TABLE \"Licenses\" ALTER COLUMN \"ValidTo\" SET NOT NULL;");
        }
    }
}
