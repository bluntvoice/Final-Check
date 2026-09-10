using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinalCheck.Data.Migrations;

/// <inheritdoc />
[DbContext(typeof(FinalCheckDbContext))]
[Migration("20260910143000_AddComparisonResults")]
public partial class AddComparisonResults : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ComparisonResults",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                ComparisonSchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                BaselineSnapshotId = table.Column<string>(type: "TEXT", nullable: false),
                CurrentSnapshotId = table.Column<string>(type: "TEXT", nullable: false),
                AlgorithmVersion = table.Column<string>(type: "TEXT", nullable: false),
                Payload = table.Column<byte[]>(type: "BLOB", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ComparisonResults", x => x.Id);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ComparisonResults");
    }
}
