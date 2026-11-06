using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CodeLearning.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class BackendReviewFixes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SubmissionTestResults_TestCases_TestCaseId",
                table: "SubmissionTestResults");

            migrationBuilder.UpdateData(
                table: "Languages",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111111"),
                column: "UpdatedAt",
                value: new DateTimeOffset(new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.UpdateData(
                table: "Languages",
                keyColumn: "Id",
                keyValue: new Guid("22222222-2222-2222-2222-222222222222"),
                column: "UpdatedAt",
                value: new DateTimeOffset(new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.UpdateData(
                table: "Languages",
                keyColumn: "Id",
                keyValue: new Guid("33333333-3333-3333-3333-333333333333"),
                column: "UpdatedAt",
                value: new DateTimeOffset(new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.UpdateData(
                table: "Languages",
                keyColumn: "Id",
                keyValue: new Guid("44444444-4444-4444-4444-444444444444"),
                column: "UpdatedAt",
                value: new DateTimeOffset(new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.UpdateData(
                table: "Tags",
                keyColumn: "Id",
                keyValue: new Guid("a1111111-1111-1111-1111-111111111111"),
                column: "UpdatedAt",
                value: new DateTimeOffset(new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.UpdateData(
                table: "Tags",
                keyColumn: "Id",
                keyValue: new Guid("a2222222-2222-2222-2222-222222222222"),
                column: "UpdatedAt",
                value: new DateTimeOffset(new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.UpdateData(
                table: "Tags",
                keyColumn: "Id",
                keyValue: new Guid("a3333333-3333-3333-3333-333333333333"),
                column: "UpdatedAt",
                value: new DateTimeOffset(new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.UpdateData(
                table: "Tags",
                keyColumn: "Id",
                keyValue: new Guid("a4444444-4444-4444-4444-444444444444"),
                column: "UpdatedAt",
                value: new DateTimeOffset(new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.UpdateData(
                table: "Tags",
                keyColumn: "Id",
                keyValue: new Guid("a5555555-5555-5555-5555-555555555555"),
                column: "UpdatedAt",
                value: new DateTimeOffset(new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.UpdateData(
                table: "Tags",
                keyColumn: "Id",
                keyValue: new Guid("a6666666-6666-6666-6666-666666666666"),
                column: "UpdatedAt",
                value: new DateTimeOffset(new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.UpdateData(
                table: "Tags",
                keyColumn: "Id",
                keyValue: new Guid("a7777777-7777-7777-7777-777777777777"),
                column: "UpdatedAt",
                value: new DateTimeOffset(new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.UpdateData(
                table: "Tags",
                keyColumn: "Id",
                keyValue: new Guid("a8888888-8888-8888-8888-888888888888"),
                column: "UpdatedAt",
                value: new DateTimeOffset(new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddForeignKey(
                name: "FK_SubmissionTestResults_TestCases_TestCaseId",
                table: "SubmissionTestResults",
                column: "TestCaseId",
                principalTable: "TestCases",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            DecodeStoredHtmlEntities(migrationBuilder);
            DeleteOrphanedBlockContent(migrationBuilder);
        }

        // Text used to be passed through an HTML sanitizer before saving, which entity-encoded it.
        // It is now stored as written, so decode what the sanitizer encoded. Content the sanitizer
        // dropped (anything after a "<type>" it read as an HTML tag) cannot be recovered
        private static void DecodeStoredHtmlEntities(MigrationBuilder migrationBuilder)
        {
            (string Table, string Column)[] columns =
            [
                ("TheoryContents", "Content"),
                ("Problems", "Description"),
                ("Courses", "Description"),
                ("QuizQuestions", "Content"),
                ("QuizQuestions", "Explanation"),
                ("QuizAnswers", "Text")
            ];

            foreach (var (table, column) in columns)
            {
                // &amp; last, so "&amp;lt;" becomes "&lt;" rather than "<"
                var decoded =
                    $"replace(replace(replace(replace(replace(replace(\"{column}\", " +
                    "'&lt;', '<'), '&gt;', '>'), '&quot;', '\"'), '&#39;', ''''), '&nbsp;', chr(160)), '&amp;', '&')";

                migrationBuilder.Sql(
                    $"UPDATE \"{table}\" SET \"{column}\" = {decoded} WHERE \"{column}\" LIKE '%&%';");
            }
        }

        // Blocks reference their content, so deleting a block used to leave the content rows behind
        private static void DeleteOrphanedBlockContent(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM "TheoryContents" t
                WHERE NOT EXISTS (SELECT 1 FROM "CourseBlocks" b WHERE b."TheoryContentId" = t."Id");
                """);

            migrationBuilder.Sql("""
                DELETE FROM "VideoContents" v
                WHERE NOT EXISTS (SELECT 1 FROM "CourseBlocks" b WHERE b."VideoContentId" = v."Id");
                """);

            // Questions and answers cascade from the quiz; a quiz with attempts is kept
            migrationBuilder.Sql("""
                DELETE FROM "Quizzes" q
                WHERE NOT EXISTS (SELECT 1 FROM "CourseBlocks" b WHERE b."QuizId" = q."Id")
                  AND NOT EXISTS (SELECT 1 FROM "StudentQuizAttempts" a WHERE a."QuizId" = q."Id");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SubmissionTestResults_TestCases_TestCaseId",
                table: "SubmissionTestResults");

            migrationBuilder.AddForeignKey(
                name: "FK_SubmissionTestResults_TestCases_TestCaseId",
                table: "SubmissionTestResults",
                column: "TestCaseId",
                principalTable: "TestCases",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
