using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CodeLearning.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class FixCSharpProjectSetup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Languages",
                keyColumn: "Id",
                keyValue: new Guid("33333333-3333-3333-3333-333333333333"),
                column: "CompileCommand",
                value: "csharp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Languages",
                keyColumn: "Id",
                keyValue: new Guid("33333333-3333-3333-3333-333333333333"),
                column: "CompileCommand",
                value: "dotnet new console -n solution --force --use-program-main && dotnet build --nologo --verbosity quiet");
        }
    }
}
