using FinalCheck.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FinalCheck.Data;

public sealed class FinalCheckDbContext(DbContextOptions<FinalCheckDbContext> options) : DbContext(options)
{
    public const int DatabaseSchemaVersion = 1;

    public DbSet<StoredDocumentSnapshot> DocumentSnapshots => Set<StoredDocumentSnapshot>();

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
    }
}
