using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace vulcanbot.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Accounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PrivatePemKey = table.Column<string>(type: "text", nullable: false),
                    CertificatePem = table.Column<string>(type: "text", nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Symbol = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RestUrl = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    UnitId = table.Column<int>(type: "integer", nullable: false),
                    UnitName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    PupilId = table.Column<int>(type: "integer", nullable: false),
                    LoginId = table.Column<int>(type: "integer", nullable: false),
                    FirstName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Surname = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ClassDisplay = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Accounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Exams",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    VulcanId = table.Column<int>(type: "integer", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    SubjectName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TeacherName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Deadline = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CalendarEventId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CalendarSyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Exams", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Exams_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Homeworks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    VulcanId = table.Column<int>(type: "integer", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    SubjectName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TeacherName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    IsAnswerRequired = table.Column<bool>(type: "boolean", nullable: false),
                    Deadline = table.Column<DateOnly>(type: "date", nullable: false),
                    AnswerDeadline = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CalendarEventId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CalendarSyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Homeworks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Homeworks_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Lessons",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    VulcanId = table.Column<int>(type: "integer", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Start = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    End = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    SubjectName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    TeacherName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Room = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Event = table.Column<string>(type: "text", nullable: true),
                    ChangeType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CalendarEventId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CalendarSyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Lessons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Lessons_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_Symbol_PupilId",
                table: "Accounts",
                columns: new[] { "Symbol", "PupilId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Exams_AccountId_Deadline",
                table: "Exams",
                columns: new[] { "AccountId", "Deadline" });

            migrationBuilder.CreateIndex(
                name: "IX_Exams_AccountId_VulcanId",
                table: "Exams",
                columns: new[] { "AccountId", "VulcanId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Homeworks_AccountId_Deadline",
                table: "Homeworks",
                columns: new[] { "AccountId", "Deadline" });

            migrationBuilder.CreateIndex(
                name: "IX_Homeworks_AccountId_VulcanId",
                table: "Homeworks",
                columns: new[] { "AccountId", "VulcanId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Lessons_AccountId_Date",
                table: "Lessons",
                columns: new[] { "AccountId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_Lessons_AccountId_VulcanId",
                table: "Lessons",
                columns: new[] { "AccountId", "VulcanId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Exams");

            migrationBuilder.DropTable(
                name: "Homeworks");

            migrationBuilder.DropTable(
                name: "Lessons");

            migrationBuilder.DropTable(
                name: "Accounts");
        }
    }
}
