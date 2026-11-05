using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace vulcanbot.Data.Migrations
{
    /// <inheritdoc />
    public partial class LessonRemovedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RemovedAt",
                table: "Lessons",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RemovedAt",
                table: "Lessons");
        }
    }
}
