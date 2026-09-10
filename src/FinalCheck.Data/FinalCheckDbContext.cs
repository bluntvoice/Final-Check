using FinalCheck.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FinalCheck.Data;

public sealed class FinalCheckDbContext(DbContextOptions<FinalCheckDbContext> options) : DbContext(options)
{
    public const int DatabaseSchemaVersion = 2;

    public DbSet<StoredDocumentSnapshot> DocumentSnapshots => Set<StoredDocumentSnapshot>();

    public DbSet<StoredComparisonResult> ComparisonResults => Set<StoredComparisonResult>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("FinalCheck:DatabaseSchemaVersion", DatabaseSchemaVersion);
        var utcDateTimeConverter = new ValueConverter<DateTime, DateTime>(
            value => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime(),
            value => DateTime.SpecifyKind(value, DateTimeKind.Utc));

        modelBuilder.Entity<StoredDocumentSnapshot>(entity =>
        {
            entity.ToTable("DocumentSnapshots");
            entity.HasKey(snapshot => snapshot.Id);
            entity.Property(snapshot => snapshot.SnapshotSchemaVersion).IsRequired();
            entity.Property(snapshot => snapshot.Payload).IsRequired();
            entity.Property(snapshot => snapshot.CreatedAtUtc)
                .HasConversion(utcDateTimeConverter)
                .IsRequired();
        });

        modelBuilder.Entity<StoredComparisonResult>(entity =>
        {
            entity.ToTable("ComparisonResults");
            entity.HasKey(comparison => comparison.Id);
            entity.Property(comparison => comparison.ComparisonSchemaVersion).IsRequired();
            entity.Property(comparison => comparison.BaselineSnapshotId).IsRequired();
            entity.Property(comparison => comparison.CurrentSnapshotId).IsRequired();
            entity.Property(comparison => comparison.AlgorithmVersion).IsRequired();
            entity.Property(comparison => comparison.Payload).IsRequired();
            entity.Property(comparison => comparison.CreatedAtUtc)
                .HasConversion(utcDateTimeConverter)
                .IsRequired();
        });
    }
}
