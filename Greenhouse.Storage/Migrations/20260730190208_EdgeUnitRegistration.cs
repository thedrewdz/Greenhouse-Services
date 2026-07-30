using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Greenhouse.Storage.Migrations
{
    /// <inheritdoc />
    public partial class EdgeUnitRegistration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EdgeUnits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DeviceId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    AdvertisedName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    UnitName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Location = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    MappingVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    MappingStatus = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    FirstSeenAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastHeartbeatAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TopologyDriftDetectedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EdgeUnits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OnboardingSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    SelectedDeviceId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OnboardingSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SlotTopologies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EdgeUnitId = table.Column<int>(type: "INTEGER", nullable: false),
                    SlotId = table.Column<int>(type: "INTEGER", nullable: false),
                    I2cAddress = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    Capability = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    Label = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ObservedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SlotTopologies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SlotTopologies_EdgeUnits_EdgeUnitId",
                        column: x => x.EdgeUnitId,
                        principalTable: "EdgeUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EdgeUnits_DeviceId",
                table: "EdgeUnits",
                column: "DeviceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SlotTopologies_EdgeUnitId_SlotId",
                table: "SlotTopologies",
                columns: new[] { "EdgeUnitId", "SlotId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OnboardingSessions");

            migrationBuilder.DropTable(
                name: "SlotTopologies");

            migrationBuilder.DropTable(
                name: "EdgeUnits");
        }
    }
}
