using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861 // EF-generated schema arrays.

namespace FinalCheck.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStageAAutomaticVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "NextVersionNumber",
                table: "Projects",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<Guid>(
                name: "BaselineContractVersionId",
                table: "ProjectComparisons",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VersionNumber",
                table: "ContractVersions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // Existing versions never had a displayed Vn label. Assign a stable
            // chronological sequence once, before enforcing project uniqueness.
            migrationBuilder.Sql("""
                UPDATE ContractVersions
                SET VersionNumber = (
                    SELECT COUNT(*) FROM ContractVersions AS earlier
                    WHERE earlier.ProjectId = ContractVersions.ProjectId
                      AND (earlier.ImportedAtUtc < ContractVersions.ImportedAtUtc
                           OR (earlier.ImportedAtUtc = ContractVersions.ImportedAtUtc AND earlier.Id <= ContractVersions.Id))
                );
                UPDATE Projects
                SET NextVersionNumber = COALESCE((
                    SELECT MAX(VersionNumber) FROM ContractVersions WHERE ContractVersions.ProjectId = Projects.Id
                ), 0) + 1;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectComparisons_BaselineContractVersionId",
                table: "ProjectComparisons",
                column: "BaselineContractVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractVersions_ProjectId_VersionNumber",
                table: "ContractVersions",
                columns: new[] { "ProjectId", "VersionNumber" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectComparisons_ContractVersions_BaselineContractVersionId",
                table: "ProjectComparisons",
                column: "BaselineContractVersionId",
                principalTable: "ContractVersions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProjectComparisons_ContractVersions_BaselineContractVersionId",
                table: "ProjectComparisons");

            migrationBuilder.DropIndex(
                name: "IX_ProjectComparisons_BaselineContractVersionId",
                table: "ProjectComparisons");

            migrationBuilder.DropIndex(
                name: "IX_ContractVersions_ProjectId_VersionNumber",
                table: "ContractVersions");

            migrationBuilder.DropColumn(
                name: "NextVersionNumber",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "BaselineContractVersionId",
                table: "ProjectComparisons");

            migrationBuilder.DropColumn(
                name: "VersionNumber",
                table: "ContractVersions");
        }
    }
}
