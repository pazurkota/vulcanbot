using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace vulcanbot.Data.Migrations
{
    /// <inheritdoc />
    public partial class LessonChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Lessons_AccountId_VulcanId",
                table: "Lessons");

            migrationBuilder.AddColumn<string>(
                name: "ChangeReason",
                table: "Lessons",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "MovedToDate",
                table: "Lessons",
                type: "date",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Lessons_AccountId_VulcanId_Date",
                table: "Lessons",
                columns: new[] { "AccountId", "VulcanId", "Date" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Lessons_AccountId_VulcanId_Date",
                table: "Lessons");

            migrationBuilder.DropColumn(
                name: "ChangeReason",
                table: "Lessons");

            migrationBuilder.DropColumn(
                name: "MovedToDate",
                table: "Lessons");

            migrationBuilder.CreateIndex(
                name: "IX_Lessons_AccountId_VulcanId",
                table: "Lessons",
                columns: new[] { "AccountId", "VulcanId" },
                unique: true);
        }
    }
}
