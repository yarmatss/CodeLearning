using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CodeLearning.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class UpdateJavaVersionAndDockerImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Languages",
                keyColumn: "Id",
                keyValue: new Guid("44444444-4444-4444-4444-444444444444"),
                columns: new[] { "DockerImage", "Version" },
                values: new object[] { "codelearning/java:21-jdk", "21" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Languages",
                keyColumn: "Id",
                keyValue: new Guid("44444444-4444-4444-4444-444444444444"),
                columns: new[] { "DockerImage", "Version" },
                values: new object[] { "codelearning/java:25-slim", "25" });
        }
    }
}
