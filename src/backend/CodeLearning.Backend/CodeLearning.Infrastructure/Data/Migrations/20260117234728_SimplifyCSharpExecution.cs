using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CodeLearning.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class SimplifyCSharpExecution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Languages",
                keyColumn: "Id",
                keyValue: new Guid("33333333-3333-3333-3333-333333333333"),
                columns: new[] { "CompileCommand", "ExecutableCommand" },
                values: new object[] { null, "dotnet run solution.cs" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Languages",
                keyColumn: "Id",
                keyValue: new Guid("33333333-3333-3333-3333-333333333333"),
                columns: new[] { "CompileCommand", "ExecutableCommand" },
                values: new object[] { "dotnet build /app/solution.csproj", "dotnet run" });
        }
    }
}
