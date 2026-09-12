using FinalCheck.Core.Storage;
using FinalCheck.Infrastructure;

namespace FinalCheck.Data.Tests;

public sealed class StorageValidationTests
{
    [Fact]
    public void NonexistentLocalFixedDirectoryCanBeCreatedAndProbed()
    {
        using var fixture = new StorageFixture();
        var target = Path.Combine(fixture.DirectoryPath, "new-data");
        var validator = Create(fixture);
        var result = validator.Validate(new(target, SourceBytes: 1024, DatabaseBytes: 512));
        Assert.True(result.IsValid, result.Code);
        Assert.True(Directory.Exists(target));
        Assert.Empty(Directory.EnumerateFileSystemEntries(target));
        Assert.Equal(64L * 1024 * 1024 + 1536, result.RequiredBytes);
    }

    [Theory]
    [InlineData("relative", "AbsolutePathRequired")]
    [InlineData("", "AbsolutePathRequired")]
    public void RelativeOrEmptyDirectoryIsRejected(string target, string code)
    {
        using var fixture = new StorageFixture();
        Assert.Equal(code, Create(fixture).Validate(new(target)).Code);
    }

    [Fact]
    public void UncIsNeverAcceptedAsSqliteRoot()
    {
        using var fixture = new StorageFixture();
        var result = Create(fixture).Validate(new(OperatingSystem.IsWindows() ? @"\\server\share\data" : "//server/share/data"));
        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData(StorageDriveKind.Network)]
    [InlineData(StorageDriveKind.Removable)]
    [InlineData(StorageDriveKind.Unknown)]
    public void UnsupportedVolumesAreBlockedBeforeCreation(StorageDriveKind kind)
    {
        using var fixture = new StorageFixture();
        var target = Path.Combine(fixture.DirectoryPath, "bad-drive");
        Assert.False(Create(fixture, kind).Validate(new(target)).IsValid);
        Assert.False(Directory.Exists(target));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadonlyOrWriteDeniedProbeIsAValidationFailure(bool readOnly)
    {
        using var fixture = new StorageFixture();
        var probe = new DeniedProbe(readOnly);
        var validator = Create(fixture, probe: probe);
        var result = validator.Validate(new(Path.Combine(fixture.DirectoryPath, "permission")));
        Assert.False(result.IsValid);
        Assert.Equal("PermissionDenied", result.Code);
        Assert.True(probe.Called);
    }

    [Fact]
    public void InstallationDataSourceAndSystemTempOverlapAreBlocked()
    {
        using var fixture = new StorageFixture();
        var validator = Create(fixture);
        Assert.Equal("InstallationOverlap", validator.Validate(new(Path.Combine(fixture.Paths.InstallDirectory, "data"))).Code);
        Assert.Equal("SourceOverlap", validator.Validate(new(Path.Combine(fixture.DirectoryPath, "source", "child"), Path.Combine(fixture.DirectoryPath, "source"))).Code);
        Assert.Equal("TemporaryDirectoryUnsupported", validator.Validate(new(Path.Combine(fixture.DirectoryPath, "system-temp", "data"))).Code);
    }

    [Fact]
    public void InstalledRootIncludesSiblingsOfReplaceableCurrentDirectory()
    {
        using var fixture = new StorageFixture();
        var installedPaths = new PlatformStoragePaths(fixture.Paths.InstallDirectory);
        var validator = new DataRootValidator(installedPaths,
            new FixtureVolumeInfoProvider(new(StorageDriveKind.Fixed, long.MaxValue, Path.Combine(fixture.DirectoryPath, "system-temp"), [])), new StorageDirectoryProbe());
        Assert.Equal("InstallationOverlap", validator.Validate(new(Path.Combine(fixture.Paths.InstallDirectory, "Data"))).Code);
        Assert.Equal(fixture.Paths.InstallDirectory, installedPaths.InstallDirectory);
    }

    [Fact]
    public void KnownSyncDirectoryIsBlockedWithoutClaimingAllProvidersAreDetected()
    {
        using var fixture = new StorageFixture();
        var sync = Path.Combine(fixture.DirectoryPath, "OneDrive");
        var result = Create(fixture, syncRoots: [sync]).Validate(new(Path.Combine(sync, "data")));
        Assert.Equal("KnownSyncDirectoryUnsupported", result.Code);
    }

    [Fact]
    public void SpaceIncludesStagingExtraBackupAndMargin()
    {
        using var fixture = new StorageFixture();
        var target = Path.Combine(fixture.DirectoryPath, "no-space");
        var result = Create(fixture, available: 1025).Validate(new(target, SourceBytes: 1024, DatabaseBytes: 512));
        Assert.Equal("InsufficientSpace", result.Code);
        Assert.True(result.RequiredBytes > 1024);
        Assert.False(Directory.Exists(target));
    }

    [Fact]
    public void NonemptyTargetCannotBeMergedOrOverwritten()
    {
        using var fixture = new StorageFixture();
        var target = Path.Combine(fixture.DirectoryPath, "user-files");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "keep.txt"), "keep");
        Assert.Equal("TargetNotEmpty", Create(fixture).Validate(new(target)).Code);
        Assert.Equal("keep", File.ReadAllText(Path.Combine(target, "keep.txt")));
    }

    [Fact]
    public void WindowsCDPolicyUsesInjectedFactsWithoutTouchingRealDrives()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new StorageFixture();
        var validator = Create(fixture, probe: new NoIoProbe());
        Assert.True(validator.Validate(new(@"C:\FinalCheck-Policy-Only\Data")).IsValid);
        Assert.True(validator.Validate(new(@"D:\FinalCheck-Policy-Only\Data")).IsValid);
    }

    private static DataRootValidator Create(StorageFixture fixture, StorageDriveKind kind = StorageDriveKind.Fixed,
        long available = long.MaxValue, IStorageDirectoryProbe? probe = null, string[]? syncRoots = null) =>
        new(fixture.Paths, new FixtureVolumeInfoProvider(new(kind, available, Path.Combine(fixture.DirectoryPath, "system-temp"), syncRoots ?? [])), probe ?? new StorageDirectoryProbe());

    private sealed class DeniedProbe(bool readOnly) : IStorageDirectoryProbe
    {
        public bool Called { get; private set; }
        public void Verify(string absoluteDirectory)
        {
            Called = true;
            throw new UnauthorizedAccessException(readOnly ? "Read-only fixture" : "Write denied fixture");
        }
    }
    private sealed class NoIoProbe : IStorageDirectoryProbe
    {
        public void Verify(string absoluteDirectory) { }
    }
}

internal sealed class FixtureVolumeInfoProvider(StorageVolumeInfo info) : IStorageVolumeInfoProvider
{
    public StorageVolumeInfo Inspect(string absolutePath) => info;
}
