using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861 // EF-generated schema arrays.

namespace FinalCheck.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectComparisonContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProjectComparisons",
                columns: table => new
                {
                    RecordId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CurrentVersionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BaselineType = table.Column<int>(type: "INTEGER", nullable: false),
                    OwnBaselineVersionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TemplateBaselineVersionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    BaselineSourceSnapshotId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CurrentSourceSnapshotId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BaselineName = table.Column<string>(type: "TEXT", nullable: false),
                    CurrentName = table.Column<string>(type: "TEXT", nullable: false),
                    TotalChanges = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectComparisons", x => x.RecordId);
                    table.ForeignKey(
                        name: "FK_ProjectComparisons_ComparisonRecords_RecordId",
                        column: x => x.RecordId,
                        principalTable: "ComparisonRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectComparisons_ContractVersions_CurrentVersionId",
                        column: x => x.CurrentVersionId,
                        principalTable: "ContractVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectComparisons_ContractVersions_OwnBaselineVersionId",
                        column: x => x.OwnBaselineVersionId,
                        principalTable: "ContractVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectComparisons_DocumentSnapshots_BaselineSourceSnapshotId",
                        column: x => x.BaselineSourceSnapshotId,
                        principalTable: "DocumentSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectComparisons_DocumentSnapshots_CurrentSourceSnapshotId",
                        column: x => x.CurrentSourceSnapshotId,
                        principalTable: "DocumentSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectComparisons_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectComparisons_TemplateVersions_TemplateBaselineVersionId",
                        column: x => x.TemplateBaselineVersionId,
                        principalTable: "TemplateVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectComparisons_BaselineSourceSnapshotId",
                table: "ProjectComparisons",
                column: "BaselineSourceSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectComparisons_CurrentSourceSnapshotId",
                table: "ProjectComparisons",
                column: "CurrentSourceSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectComparisons_CurrentVersionId",
                table: "ProjectComparisons",
                column: "CurrentVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectComparisons_OwnBaselineVersionId",
                table: "ProjectComparisons",
                column: "OwnBaselineVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectComparisons_ProjectId_CreatedAtUtc",
                table: "ProjectComparisons",
                columns: new[] { "ProjectId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectComparisons_TemplateBaselineVersionId",
                table: "ProjectComparisons",
                column: "TemplateBaselineVersionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProjectComparisons");
        }
    }
}
