using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinalCheck.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIndependentComparisonRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ComparisonRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BaselineSnapshotId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CurrentSnapshotId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ResultId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Payload = table.Column<byte[]>(type: "BLOB", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComparisonRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ComparisonRecords_ComparisonResults_ResultId",
                        column: x => x.ResultId,
                        principalTable: "ComparisonResults",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ComparisonRecords_DocumentSnapshots_BaselineSnapshotId",
                        column: x => x.BaselineSnapshotId,
                        principalTable: "DocumentSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ComparisonRecords_DocumentSnapshots_CurrentSnapshotId",
                        column: x => x.CurrentSnapshotId,
                        principalTable: "DocumentSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ComparisonRecords_BaselineSnapshotId",
                table: "ComparisonRecords",
                column: "BaselineSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_ComparisonRecords_CreatedAtUtc",
                table: "ComparisonRecords",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ComparisonRecords_CurrentSnapshotId",
                table: "ComparisonRecords",
                column: "CurrentSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_ComparisonRecords_ResultId",
                table: "ComparisonRecords",
                column: "ResultId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ComparisonRecords");
        }
    }
}
