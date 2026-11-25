using FactoryService.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FactoryService.Api.Data.Migrations;

[DbContext(typeof(FactoryDbContext))]
[Migration("20261125120000_AddSurveyResponseCarPlate")]
public partial class AddSurveyResponseCarPlate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "CarPlate",
            table: "SurveyResponses",
            type: "nvarchar(20)",
            maxLength: 20,
            nullable: false,
            defaultValue: "");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "CarPlate",
            table: "SurveyResponses");
    }
}
