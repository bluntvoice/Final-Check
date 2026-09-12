using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FinalCheck.Data.Migrations;

[DbContext(typeof(FinalCheckDbContext))]
[Migration("20260912110000_AddFormatRestoreHistory")]
public sealed class AddFormatRestoreHistory : Migration
{
    private static readonly string[] OperationIndexColumns = ["ContractVersionId", "CreatedAtUtc"];
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("RestoredWorkingCopies", columns: table => new
        {
            ContractVersionId = table.Column<Guid>(type: "TEXT", nullable: false), Sha256 = table.Column<string>(type: "TEXT", nullable: false),
            Payload = table.Column<byte[]>(type: "BLOB", nullable: false), UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
        }, constraints: table => table.PrimaryKey("PK_RestoredWorkingCopies", x => x.ContractVersionId));
        migrationBuilder.CreateTable("FormatRestoreOperations", columns: table => new
        {
            Id = table.Column<Guid>(type: "TEXT", nullable: false), ContractVersionId = table.Column<Guid>(type: "TEXT", nullable: false),
            SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false), Status = table.Column<string>(type: "TEXT", nullable: false),
            Payload = table.Column<byte[]>(type: "BLOB", nullable: false), CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
        }, constraints: table => table.PrimaryKey("PK_FormatRestoreOperations", x => x.Id));
        migrationBuilder.CreateIndex("IX_FormatRestoreOperations_ContractVersionId_CreatedAtUtc", "FormatRestoreOperations", OperationIndexColumns);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("FormatRestoreOperations");
        migrationBuilder.DropTable("RestoredWorkingCopies");
    }
}
