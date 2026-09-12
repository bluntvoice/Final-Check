using FinalCheck.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using FinalCheck.Core.Storage;

namespace FinalCheck.Data;

public sealed class FinalCheckDbContext(DbContextOptions<FinalCheckDbContext> options,
    IStorageDataSession? storageSession = null) : DbContext(options)
{
    public IDataRootProvider ManagedPaths => storageSession ?? throw new InvalidOperationException("This context is not attached to a managed storage session.");
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        storageSession?.EnsureActive();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        storageSession?.EnsureActive();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
    public override void Dispose()
    {
        try { base.Dispose(); } finally { storageSession?.Dispose(); }
    }
    public override async ValueTask DisposeAsync()
    {
        try { await base.DisposeAsync(); } finally { storageSession?.Dispose(); }
    }
    public const int DatabaseSchemaVersion = 3;

    public DbSet<StoredDocumentSnapshot> DocumentSnapshots => Set<StoredDocumentSnapshot>();

    public DbSet<StoredComparisonResult> ComparisonResults => Set<StoredComparisonResult>();

    public DbSet<StoredRestoredWorkingCopy> RestoredWorkingCopies => Set<StoredRestoredWorkingCopy>();

    public DbSet<StoredFormatRestoreOperation> FormatRestoreOperations => Set<StoredFormatRestoreOperation>();

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
        modelBuilder.Entity<StoredRestoredWorkingCopy>(entity =>
        {
            entity.ToTable("RestoredWorkingCopies");
            entity.HasKey(copy => copy.ContractVersionId);
            entity.Property(copy => copy.Sha256).IsRequired().IsConcurrencyToken();
            entity.Property(copy => copy.Payload).IsRequired();
            entity.Property(copy => copy.UpdatedAtUtc).HasConversion(utcDateTimeConverter).IsRequired();
        });
        modelBuilder.Entity<StoredFormatRestoreOperation>(entity =>
        {
            entity.ToTable("FormatRestoreOperations");
            entity.HasKey(operation => operation.Id);
            entity.HasIndex(operation => new { operation.ContractVersionId, operation.CreatedAtUtc });
            entity.Property(operation => operation.Status).IsRequired();
            entity.Property(operation => operation.Payload).IsRequired();
            entity.Property(operation => operation.CreatedAtUtc).HasConversion(utcDateTimeConverter).IsRequired();
        });
    }
}
