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
    public const int DatabaseSchemaVersion = 6;
    public DbSet<StoredProject> Projects => Set<StoredProject>();
    public DbSet<StoredProjectFolder> ProjectFolders => Set<StoredProjectFolder>();
    public DbSet<StoredTemplate> Templates => Set<StoredTemplate>();
    public DbSet<StoredTemplateVersion> TemplateVersions => Set<StoredTemplateVersion>();
    public DbSet<StoredComparisonRecord> ComparisonRecords => Set<StoredComparisonRecord>();

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

        modelBuilder.Entity<StoredProjectFolder>(entity =>
        {
            entity.ToTable("ProjectFolders"); entity.HasKey(x => x.Id); entity.Property(x => x.Name).IsRequired(); entity.HasIndex(x => x.Name).IsUnique();
        });
        modelBuilder.Entity<StoredProject>(entity =>
        {
            entity.ToTable("Projects"); entity.HasKey(x => x.Id);
            entity.Property(x => x.ProjectName).IsRequired(); entity.Property(x => x.Counterparty).IsRequired(); entity.Property(x => x.ContractType).IsRequired();
            entity.Property(x => x.TagsJson).IsRequired(); entity.Property(x => x.Notes).IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasConversion(utcDateTimeConverter); entity.Property(x => x.UpdatedAtUtc).HasConversion(utcDateTimeConverter);
            entity.HasIndex(x => new { x.Status, x.UpdatedAtUtc });
            entity.HasOne<StoredTemplate>().WithMany().HasForeignKey(x => x.BoundTemplateId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<StoredTemplateVersion>().WithMany().HasForeignKey(x => x.BoundTemplateVersionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<StoredProjectFolder>().WithMany().HasForeignKey(x => x.FolderId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<StoredTemplate>(entity =>
        {
            entity.ToTable("Templates"); entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).IsRequired(); entity.Property(x => x.ContractType).IsRequired(); entity.Property(x => x.Notes).IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasConversion(utcDateTimeConverter);
            entity.Property(x => x.UpdatedAtUtc).HasConversion(utcDateTimeConverter);
            entity.HasIndex(x => new { x.IsDeleted, x.UpdatedAtUtc });
        });
        modelBuilder.Entity<StoredTemplateVersion>(entity =>
        {
            entity.ToTable("TemplateVersions"); entity.HasKey(x => x.Id);
            entity.Property(x => x.Version).IsRequired(); entity.Property(x => x.FilePath).IsRequired(); entity.Property(x => x.FileName).IsRequired(); entity.Property(x => x.Sha256).IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasConversion(utcDateTimeConverter); entity.Property(x => x.ModifiedAtUtc).HasConversion(utcDateTimeConverter);
            entity.HasIndex(x => new { x.TemplateId, x.Version }).IsUnique();
            entity.HasIndex(x => x.TemplateId).IsUnique().HasFilter("IsCurrent = 1");
            entity.HasOne<StoredTemplate>().WithMany().HasForeignKey(x => x.TemplateId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<StoredDocumentSnapshot>().WithMany().HasForeignKey(x => x.SnapshotId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<StoredComparisonRecord>(entity =>
        {
            entity.ToTable("ComparisonRecords"); entity.HasKey(record => record.Id);
            entity.Property(record => record.Payload).IsRequired();
            entity.Property(record => record.CreatedAtUtc).HasConversion(utcDateTimeConverter).IsRequired();
            entity.HasIndex(record => record.CreatedAtUtc);
            entity.HasOne<StoredDocumentSnapshot>().WithMany().HasForeignKey(record => record.BaselineSnapshotId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<StoredDocumentSnapshot>().WithMany().HasForeignKey(record => record.CurrentSnapshotId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<StoredComparisonResult>().WithMany().HasForeignKey(record => record.ResultId).OnDelete(DeleteBehavior.Restrict);
        });
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
