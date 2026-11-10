using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CodeLearning.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReviewFixesRound2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Certificates");

            migrationBuilder.DropTable(
                name: "Comments");

            migrationBuilder.DropIndex(
                name: "IX_SubmissionTestResults_SubmissionId",
                table: "SubmissionTestResults");

            migrationBuilder.DropIndex(
                name: "IX_StarterCodes_ProblemId",
                table: "StarterCodes");

            // Remove duplicate rows before the unique indexes are created; keep the newest row per key
            migrationBuilder.Sql("""
                DELETE FROM "SubmissionTestResults"
                WHERE "Id" IN (
                    SELECT "Id" FROM (
                        SELECT "Id", ROW_NUMBER() OVER (
                            PARTITION BY "SubmissionId", "TestCaseId"
                            ORDER BY "CreatedAt" DESC, "Id" DESC) AS rn
                        FROM "SubmissionTestResults") d
                    WHERE d.rn > 1);
                """);

            migrationBuilder.Sql("""
                DELETE FROM "StarterCodes"
                WHERE "Id" IN (
                    SELECT "Id" FROM (
                        SELECT "Id", ROW_NUMBER() OVER (
                            PARTITION BY "ProblemId", "LanguageId"
                            ORDER BY "UpdatedAt" DESC, "Id" DESC) AS rn
                        FROM "StarterCodes") d
                    WHERE d.rn > 1);
                """);

            migrationBuilder.CreateIndex(
                name: "IX_SubmissionTestResults_SubmissionId_TestCaseId",
                table: "SubmissionTestResults",
                columns: new[] { "SubmissionId", "TestCaseId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StarterCodes_ProblemId_LanguageId",
                table: "StarterCodes",
                columns: new[] { "ProblemId", "LanguageId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SubmissionTestResults_SubmissionId_TestCaseId",
                table: "SubmissionTestResults");

            migrationBuilder.DropIndex(
                name: "IX_StarterCodes_ProblemId_LanguageId",
                table: "StarterCodes");

            migrationBuilder.CreateTable(
                name: "Certificates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CourseId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    CertificateUrl = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    IssuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    VerificationCode = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Certificates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Certificates_Courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "Courses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Certificates_Users_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Comments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BlockId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentCommentId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Comments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Comments_Comments_ParentCommentId",
                        column: x => x.ParentCommentId,
                        principalTable: "Comments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Comments_CourseBlocks_BlockId",
                        column: x => x.BlockId,
                        principalTable: "CourseBlocks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Comments_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubmissionTestResults_SubmissionId",
                table: "SubmissionTestResults",
                column: "SubmissionId");

            migrationBuilder.CreateIndex(
                name: "IX_StarterCodes_ProblemId",
                table: "StarterCodes",
                column: "ProblemId");

            migrationBuilder.CreateIndex(
                name: "IX_Certificates_CourseId",
                table: "Certificates",
                column: "CourseId");

            migrationBuilder.CreateIndex(
                name: "IX_Certificates_StudentId",
                table: "Certificates",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_Certificates_VerificationCode",
                table: "Certificates",
                column: "VerificationCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Comments_BlockId",
                table: "Comments",
                column: "BlockId");

            migrationBuilder.CreateIndex(
                name: "IX_Comments_ParentCommentId",
                table: "Comments",
                column: "ParentCommentId");

            migrationBuilder.CreateIndex(
                name: "IX_Comments_UserId",
                table: "Comments",
                column: "UserId");
        }
    }
}
