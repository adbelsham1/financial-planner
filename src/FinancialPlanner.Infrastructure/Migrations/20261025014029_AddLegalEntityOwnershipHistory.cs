using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialPlanner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLegalEntityOwnershipHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "LegalEntities",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ArchivedAt",
                table: "LegalEntities",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OwnershipContributions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LegalEntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReversesOwnershipContributionId = table.Column<Guid>(type: "uuid", nullable: true),
                    PartyName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EffectiveOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OwnershipContributions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OwnershipContributions_LegalEntities_LegalEntityId",
                        column: x => x.LegalEntityId,
                        principalTable: "LegalEntities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OwnershipContributions_OwnershipContributions_ReversesOwner~",
                        column: x => x.ReversesOwnershipContributionId,
                        principalTable: "OwnershipContributions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OwnershipSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LegalEntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupersedesOwnershipSnapshotId = table.Column<Guid>(type: "uuid", nullable: true),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OwnershipSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OwnershipSnapshots_LegalEntities_LegalEntityId",
                        column: x => x.LegalEntityId,
                        principalTable: "LegalEntities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OwnershipSnapshots_OwnershipSnapshots_SupersedesOwnershipSn~",
                        column: x => x.SupersedesOwnershipSnapshotId,
                        principalTable: "OwnershipSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OwnershipParties",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnershipSnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Percentage = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    IsWorkspaceOwner = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OwnershipParties", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OwnershipParties_OwnershipSnapshots_OwnershipSnapshotId",
                        column: x => x.OwnershipSnapshotId,
                        principalTable: "OwnershipSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LegalEntities_TenantId_Name",
                table: "LegalEntities",
                columns: new[] { "TenantId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_OwnershipContributions_LegalEntityId_EffectiveOn",
                table: "OwnershipContributions",
                columns: new[] { "LegalEntityId", "EffectiveOn" });

            migrationBuilder.CreateIndex(
                name: "IX_OwnershipContributions_ReversesOwnershipContributionId",
                table: "OwnershipContributions",
                column: "ReversesOwnershipContributionId");

            migrationBuilder.CreateIndex(
                name: "IX_OwnershipParties_OwnershipSnapshotId",
                table: "OwnershipParties",
                column: "OwnershipSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_OwnershipSnapshots_LegalEntityId_EffectiveFrom",
                table: "OwnershipSnapshots",
                columns: new[] { "LegalEntityId", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_OwnershipSnapshots_SupersedesOwnershipSnapshotId",
                table: "OwnershipSnapshots",
                column: "SupersedesOwnershipSnapshotId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OwnershipContributions");

            migrationBuilder.DropTable(
                name: "OwnershipParties");

            migrationBuilder.DropTable(
                name: "OwnershipSnapshots");

            migrationBuilder.DropIndex(
                name: "IX_LegalEntities_TenantId_Name",
                table: "LegalEntities");

            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                table: "LegalEntities");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "LegalEntities",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256);
        }
    }
}
