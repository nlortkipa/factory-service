using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FactoryService.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemovePlantCodeAndOperatorAdUsername : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Plants_Code",
                table: "Plants");

            migrationBuilder.DropIndex(
                name: "IX_Operators_AdUsername",
                table: "Operators");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "Plants");

            migrationBuilder.DropColumn(
                name: "AdUsername",
                table: "Operators");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "Plants",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AdUsername",
                table: "Operators",
                type: "nvarchar(160)",
                maxLength: 160,
                nullable: true);

            if (ActiveProvider.Contains("SqlServer", StringComparison.Ordinal))
            {
                migrationBuilder.Sql("UPDATE [Plants] SET [Code] = LOWER(REPLACE(CONVERT(nvarchar(36), [Id]), '-', ''));");
                migrationBuilder.Sql("UPDATE [Operators] SET [AdUsername] = CONCAT('legacy-', LOWER(CONVERT(nvarchar(36), [Id])));");
            }
            else
            {
                migrationBuilder.Sql("UPDATE Plants SET Code = lower(replace(Id, '-', ''));");
                migrationBuilder.Sql("UPDATE Operators SET AdUsername = 'legacy-' || lower(Id);");
            }

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "Plants",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(32)",
                oldMaxLength: 32,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "AdUsername",
                table: "Operators",
                type: "nvarchar(160)",
                maxLength: 160,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(160)",
                oldMaxLength: 160,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Plants_Code",
                table: "Plants",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Operators_AdUsername",
                table: "Operators",
                column: "AdUsername",
                unique: true);
        }
    }
}
