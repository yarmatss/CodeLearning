using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CodeLearning.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddExecutableCommandToLanguage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExecutableCommand",
                table: "Languages",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.UpdateData(
                table: "Languages",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111111"),
                columns: new[] { "ExecutableCommand", "RunCommand" },
                values: new object[] { "python3", "/bin/bash /app/run_tests.sh" });

            migrationBuilder.UpdateData(
                table: "Languages",
                keyColumn: "Id",
                keyValue: new Guid("22222222-2222-2222-2222-222222222222"),
                columns: new[] { "ExecutableCommand", "RunCommand" },
                values: new object[] { "node", "/bin/bash /app/run_tests.sh" });

            migrationBuilder.UpdateData(
                table: "Languages",
                keyColumn: "Id",
                keyValue: new Guid("33333333-3333-3333-3333-333333333333"),
                columns: new[] { "CompileCommand", "ExecutableCommand", "RunCommand" },
                values: new object[] { "dotnet build /app/solution.csproj", "dotnet run", "/bin/bash /app/run_tests.sh" });

            migrationBuilder.UpdateData(
                table: "Languages",
                keyColumn: "Id",
                keyValue: new Guid("44444444-4444-4444-4444-444444444444"),
                columns: new[] { "CompileCommand", "ExecutableCommand", "RunCommand" },
                values: new object[] { "javac solution.java", "java Solution", "/bin/bash /app/run_tests.sh" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExecutableCommand",
                table: "Languages");

            migrationBuilder.UpdateData(
                table: "Languages",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111111"),
                column: "RunCommand",
                value: "python3 /app/runner.py");

            migrationBuilder.UpdateData(
                table: "Languages",
                keyColumn: "Id",
                keyValue: new Guid("22222222-2222-2222-2222-222222222222"),
                column: "RunCommand",
                value: "node /app/runner.js");

            migrationBuilder.UpdateData(
                table: "Languages",
                keyColumn: "Id",
                keyValue: new Guid("33333333-3333-3333-3333-333333333333"),
                columns: new[] { "CompileCommand", "RunCommand" },
                values: new object[] { "dotnet build /app/runner.csproj", "dotnet run --project /app/runner.csproj" });

            migrationBuilder.UpdateData(
                table: "Languages",
                keyColumn: "Id",
                keyValue: new Guid("44444444-4444-4444-4444-444444444444"),
                columns: new[] { "CompileCommand", "RunCommand" },
                values: new object[] { "javac /app/Runner.java", "java /app/Runner.java" });
        }
    }
}
