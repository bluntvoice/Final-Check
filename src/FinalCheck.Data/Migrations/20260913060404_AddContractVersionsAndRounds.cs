using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861 // EF-generated schema arrays.

namespace FinalCheck.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddContractVersionsAndRounds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NegotiationRounds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Number = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NegotiationRounds", x => x.Id);
                    table.UniqueConstraint("AK_NegotiationRounds_ProjectId_Number", x => new { x.ProjectId, x.Number });
                    table.ForeignKey(
                        name: "FK_NegotiationRounds_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ContractVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FilePath = table.Column<string>(type: "TEXT", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", nullable: false),
                    FileSize = table.Column<long>(type: "INTEGER", nullable: false),
                    ModifiedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", nullable: false),
                    SnapshotId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Role = table.Column<int>(type: "INTEGER", nullable: false),
                    RoundNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    ImportedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: false),
                    OriginalSourceJson = table.Column<string>(type: "TEXT", nullable: false),
                    DuplicateReference = table.Column<Guid>(type: "TEXT", nullable: true),
                    ParseStatus = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContractVersions_DocumentSnapshots_SnapshotId",
                        column: x => x.SnapshotId,
                        principalTable: "DocumentSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContractVersions_NegotiationRounds_ProjectId_RoundNumber",
                        columns: x => new { x.ProjectId, x.RoundNumber },
                        principalTable: "NegotiationRounds",
                        principalColumns: new[] { "ProjectId", "Number" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContractVersions_ProjectId_RoundNumber_ImportedAtUtc",
                table: "ContractVersions",
                columns: new[] { "ProjectId", "RoundNumber", "ImportedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ContractVersions_ProjectId_Sha256",
                table: "ContractVersions",
                columns: new[] { "ProjectId", "Sha256" });

            migrationBuilder.CreateIndex(
                name: "IX_ContractVersions_SnapshotId",
                table: "ContractVersions",
                column: "SnapshotId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContractVersions");

            migrationBuilder.DropTable(
                name: "NegotiationRounds");
        }
    }
}
