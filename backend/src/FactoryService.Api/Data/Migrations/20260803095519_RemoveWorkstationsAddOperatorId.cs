using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FactoryService.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveWorkstationsAddOperatorId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Operators_Workstations_WorkstationId",
                table: "Operators");

            migrationBuilder.DropForeignKey(
                name: "FK_QrSessions_Workstations_WorkstationId",
                table: "QrSessions");

            migrationBuilder.DropForeignKey(
                name: "FK_SurveyResponses_Workstations_WorkstationId",
                table: "SurveyResponses");

            migrationBuilder.DropTable(
                name: "Workstations");

            migrationBuilder.DropIndex(
                name: "IX_SurveyResponses_WorkstationId",
                table: "SurveyResponses");

            migrationBuilder.DropIndex(
                name: "IX_QrSessions_WorkstationId",
                table: "QrSessions");

            migrationBuilder.DropIndex(
                name: "IX_Operators_WorkstationId",
                table: "Operators");

            migrationBuilder.DropColumn(
                name: "WorkstationId",
                table: "SurveyResponses");

            migrationBuilder.DropColumn(
                name: "WorkstationId",
                table: "QrSessions");

            migrationBuilder.DropColumn(
                name: "WorkstationId",
                table: "Operators");

            migrationBuilder.AddColumn<string>(
                name: "OperatorId",
                table: "Operators",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            if (ActiveProvider.Contains("SqlServer", StringComparison.Ordinal))
                migrationBuilder.Sql("UPDATE [Operators] SET [OperatorId] = LOWER(REPLACE(CONVERT(nvarchar(36), [Id]), '-', '')) WHERE [OperatorId] IS NULL;");
            else
                migrationBuilder.Sql("UPDATE Operators SET OperatorId = lower(replace(Id, '-', '')) WHERE OperatorId IS NULL;");

            migrationBuilder.AlterColumn<string>(
                name: "OperatorId",
                table: "Operators",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Operators_OperatorId",
                table: "Operators",
                column: "OperatorId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Operators_OperatorId",
                table: "Operators");

            migrationBuilder.DropColumn(
                name: "OperatorId",
                table: "Operators");

            migrationBuilder.AddColumn<Guid>(
                name: "WorkstationId",
                table: "SurveyResponses",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "WorkstationId",
                table: "QrSessions",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "WorkstationId",
                table: "Operators",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Workstations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActiveWindowsUser = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    AgentKeyHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ClientVersion = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ComputerName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DeviceId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    LastErrorCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    LastQrRenderedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastSeen = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ScreenModel = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Serial = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TerminalNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Workstations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Workstations_Plants_PlantId",
                        column: x => x.PlantId,
                        principalTable: "Plants",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_SurveyResponses_WorkstationId",
                table: "SurveyResponses",
                column: "WorkstationId");

            migrationBuilder.CreateIndex(
                name: "IX_QrSessions_WorkstationId",
                table: "QrSessions",
                column: "WorkstationId");

            migrationBuilder.CreateIndex(
                name: "IX_Operators_WorkstationId",
                table: "Operators",
                column: "WorkstationId");

            migrationBuilder.CreateIndex(
                name: "IX_Workstations_DeviceId",
                table: "Workstations",
                column: "DeviceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Workstations_PlantId",
                table: "Workstations",
                column: "PlantId");

            migrationBuilder.AddForeignKey(
                name: "FK_Operators_Workstations_WorkstationId",
                table: "Operators",
                column: "WorkstationId",
                principalTable: "Workstations",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_QrSessions_Workstations_WorkstationId",
                table: "QrSessions",
                column: "WorkstationId",
                principalTable: "Workstations",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_SurveyResponses_Workstations_WorkstationId",
                table: "SurveyResponses",
                column: "WorkstationId",
                principalTable: "Workstations",
                principalColumn: "Id");
        }
    }
}
