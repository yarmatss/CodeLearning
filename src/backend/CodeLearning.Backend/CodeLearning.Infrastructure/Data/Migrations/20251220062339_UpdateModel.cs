using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CodeLearning.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class UpdateModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Languages",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111111"),
                columns: new[] { "DockerImage", "RunCommand", "Version" },
                values: new object[] { "python:3.14.2-alpine", "python3 /app/runner.py", "3.14.2" });

            migrationBuilder.UpdateData(
                table: "Languages",
                keyColumn: "Id",
                keyValue: new Guid("22222222-2222-2222-2222-222222222222"),
                columns: new[] { "DockerImage", "RunCommand", "Version" },
                values: new object[] { "node:25.2.1-alpine", "node /app/runner.js", "25.2.1" });

            migrationBuilder.UpdateData(
                table: "Languages",
                keyColumn: "Id",
                keyValue: new Guid("33333333-3333-3333-3333-333333333333"),
                columns: new[] { "CompileCommand", "RunCommand", "Version" },
                values: new object[] { "dotnet build /app/runner.csproj", "dotnet run --project /app/runner.csproj", "14" });

            migrationBuilder.UpdateData(
                table: "Languages",
                keyColumn: "Id",
                keyValue: new Guid("44444444-4444-4444-4444-444444444444"),
                columns: new[] { "CompileCommand", "DockerImage", "RunCommand", "Version" },
                values: new object[] { "javac /app/Runner.java", "openjdk:25-slim", "java /app/Runner.java", "25" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Languages",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111111"),
                columns: new[] { "DockerImage", "RunCommand", "Version" },
                values: new object[] { "python:3.11-alpine", "python3 /app/solution.py", "3.11" });

            migrationBuilder.UpdateData(
                table: "Languages",
                keyColumn: "Id",
                keyValue: new Guid("22222222-2222-2222-2222-222222222222"),
                columns: new[] { "DockerImage", "RunCommand", "Version" },
                values: new object[] { "node:20-alpine", "node /app/solution.js", "20" });

            migrationBuilder.UpdateData(
                table: "Languages",
                keyColumn: "Id",
                keyValue: new Guid("33333333-3333-3333-3333-333333333333"),
                columns: new[] { "CompileCommand", "RunCommand", "Version" },
                values: new object[] { "dotnet build /app/solution.csproj", "dotnet run --project /app/solution.csproj", "12" });

            migrationBuilder.UpdateData(
                table: "Languages",
                keyColumn: "Id",
                keyValue: new Guid("44444444-4444-4444-4444-444444444444"),
                columns: new[] { "CompileCommand", "DockerImage", "RunCommand", "Version" },
                values: new object[] { "javac /app/Solution.java", "openjdk:21-slim", "java /app/Solution.java", "21" });
        }
    }
}
