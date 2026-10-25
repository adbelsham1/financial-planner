using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialPlanner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditableLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LedgerAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    LegalEntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LedgerAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LedgerAccounts_LegalEntities_LegalEntityId",
                        column: x => x.LegalEntityId,
                        principalTable: "LegalEntities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LedgerJournals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    LegalEntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    EffectiveOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ApprovedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReversesJournalId = table.Column<Guid>(type: "uuid", nullable: true),
                    AdjustsJournalId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LedgerJournals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LedgerJournals_LedgerJournals_AdjustsJournalId",
                        column: x => x.AdjustsJournalId,
                        principalTable: "LedgerJournals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LedgerJournals_LedgerJournals_ReversesJournalId",
                        column: x => x.ReversesJournalId,
                        principalTable: "LedgerJournals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LedgerJournals_LegalEntities_LegalEntityId",
                        column: x => x.LegalEntityId,
                        principalTable: "LegalEntities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LedgerJournalLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LedgerJournalId = table.Column<Guid>(type: "uuid", nullable: false),
                    LedgerAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Debit = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Credit = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    GstTaxTreatment = table.Column<int>(type: "integer", nullable: false),
                    GstRate = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LedgerJournalLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LedgerJournalLines_LedgerAccounts_LedgerAccountId",
                        column: x => x.LedgerAccountId,
                        principalTable: "LedgerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LedgerJournalLines_LedgerJournals_LedgerJournalId",
                        column: x => x.LedgerJournalId,
                        principalTable: "LedgerJournals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LedgerAccounts_LegalEntityId_Code",
                table: "LedgerAccounts",
                columns: new[] { "LegalEntityId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LedgerAccounts_TenantId_LegalEntityId",
                table: "LedgerAccounts",
                columns: new[] { "TenantId", "LegalEntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_LedgerJournalLines_LedgerAccountId",
                table: "LedgerJournalLines",
                column: "LedgerAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerJournalLines_LedgerJournalId",
                table: "LedgerJournalLines",
                column: "LedgerJournalId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerJournals_AdjustsJournalId",
                table: "LedgerJournals",
                column: "AdjustsJournalId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerJournals_LegalEntityId_EffectiveOn_Status",
                table: "LedgerJournals",
                columns: new[] { "LegalEntityId", "EffectiveOn", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_LedgerJournals_ReversesJournalId",
                table: "LedgerJournals",
                column: "ReversesJournalId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LedgerJournalLines");

            migrationBuilder.DropTable(
                name: "LedgerAccounts");

            migrationBuilder.DropTable(
                name: "LedgerJournals");
        }
    }
}
