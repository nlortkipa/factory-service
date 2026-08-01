using FactoryService.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FactoryService.Api.Data.Migrations;

[DbContext(typeof(FactoryDbContext))]
[Migration("20260801140000_RemovePublicDisplayName")]
public partial class RemovePublicDisplayName : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "PublicDisplayName",
            table: "Operators");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "PublicDisplayName",
            table: "Operators",
            type: "nvarchar(80)",
            maxLength: 80,
            nullable: false,
            defaultValue: "");
    }
}
