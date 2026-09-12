using FinalCheck.Core.Storage;

namespace FinalCheck.Infrastructure;

/// <summary>Scope-lifetime barrier shared by DB and Working Copy; cooperative processes serialize data sessions.</summary>
public sealed class StorageMaintenanceCoordinator(IDataRootProvider initial, IPlatformStoragePaths platform,
    IStorageBootstrapStore bootstrap, DataRootBootstrapResolver resolver) : IDataRootProvider, IStorageMaintenanceCoordinator, IDisposable
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _maintenanceSerial = new(1, 1);
    private IDataRootProvider _current = new DataRootPaths(initial.Descriptor);
    private TaskCompletionSource _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private FileStream? _processLease;
    private int _active;
    private bool _pending;
    private bool _recoveryRequired;
    private bool _disposed;
    public DataRootDescriptor Descriptor => Volatile.Read(ref _current).Descriptor;
    public string CurrentDataRoot => Volatile.Read(ref _current).CurrentDataRoot;
    public string DatabasePath => Volatile.Read(ref _current).DatabasePath;
    public string SnapshotPath => Volatile.Read(ref _current).SnapshotPath;
    public string WorkingCopyPath => Volatile.Read(ref _current).WorkingCopyPath;
    public string BackupPath => Volatile.Read(ref _current).BackupPath;
    public string CachePath => Volatile.Read(ref _current).CachePath;
    public string LogPath => Volatile.Read(ref _current).LogPath;
    public string TempPath => Volatile.Read(ref _current).TempPath;

    public IStorageDataSession OpenSession()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_pending || _recoveryRequired) throw new InvalidOperationException("Storage maintenance/recovery blocks new data sessions.");
            if (_active == 0)
            {
                _processLease = OpenProcessLease();
                try
                {
                    var latest = bootstrap.Load() ?? throw new InvalidDataException("Storage bootstrap is missing.");
                    if (latest.Current != Descriptor)
                        Volatile.Write(ref _current, resolver.ResolveAsync().GetAwaiter().GetResult().Provider);
                    _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
                }
                catch { _processLease.Dispose(); _processLease = null; throw; }
            }
            _active++;
            return new DataSession(this, new DataRootPaths(Descriptor));
        }
    }

    public async Task<IStorageMaintenanceLease> EnterMaintenanceAsync(CancellationToken cancellationToken = default)
    {
        await _maintenanceSerial.WaitAsync(cancellationToken);
        try
        {
            Task drain;
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_recoveryRequired) throw new InvalidOperationException("Storage recovery is required.");
                _pending = true;
                drain = _active == 0 ? Task.CompletedTask : _drained.Task;
            }
            // Scope owners must finish/dispose. Never silently invalidate a live transaction or file pipeline.
            await drain.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            lock (_gate)
            {
                _processLease = OpenProcessLease();
                var latest = bootstrap.Load() ?? throw new InvalidDataException("Storage bootstrap is missing.");
                if (latest.Current != Descriptor) Volatile.Write(ref _current, resolver.ResolveAsync(cancellationToken).GetAwaiter().GetResult().Provider);
                return new MaintenanceLease(this, Descriptor);
            }
        }
        catch
        {
            lock (_gate)
            {
                _pending = false;
                if (_active == 0) { _processLease?.Dispose(); _processLease = null; }
            }
            _maintenanceSerial.Release();
            throw;
        }
    }

    private FileStream OpenProcessLease()
    {
        var location = Path.Combine(platform.ConfigurationDirectory, "storage-session.lock");
        StorageFileSafety.RejectLinks(location);
        Directory.CreateDirectory(platform.ConfigurationDirectory);
        return new(location, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    private void ReleaseSession()
    {
        lock (_gate)
        {
            if (--_active == 0)
            {
                _processLease?.Dispose(); _processLease = null;
                _drained.TrySetResult();
            }
        }
    }
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            if (_active != 0 || _pending) throw new InvalidOperationException("Dispose data/maintenance scopes before the storage coordinator.");
            _disposed = true;
            _processLease?.Dispose();
            _maintenanceSerial.Dispose();
        }
    }

    private sealed class DataSession(StorageMaintenanceCoordinator owner, IDataRootProvider paths) : IStorageDataSession
    {
        private int _disposed;
        public void EnsureActive()
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (owner.Descriptor != paths.Descriptor) throw new InvalidOperationException("Stale storage generation.");
        }
        private IDataRootProvider Active { get { EnsureActive(); return paths; } }
        public DataRootDescriptor Descriptor => Active.Descriptor;
        public string CurrentDataRoot => Active.CurrentDataRoot;
        public string DatabasePath => Active.DatabasePath;
        public string SnapshotPath => Active.SnapshotPath;
        public string WorkingCopyPath => Active.WorkingCopyPath;
        public string BackupPath => Active.BackupPath;
        public string CachePath => Active.CachePath;
        public string LogPath => Active.LogPath;
        public string TempPath => Active.TempPath;
        public void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) == 0) owner.ReleaseSession(); }
    }

    private sealed class MaintenanceLease(StorageMaintenanceCoordinator owner, DataRootDescriptor previous) : IStorageMaintenanceLease
    {
        private bool _completed;
        private bool _disposed;
        public void StageRoot(DataRootDescriptor descriptor)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (descriptor.Generation != previous.Generation + 1 || descriptor.RootId == previous.RootId)
                throw new InvalidDataException("A migrated root requires a new identity and next generation.");
            Volatile.Write(ref owner._current, new DataRootPaths(descriptor));
        }
        public void Complete() { ObjectDisposedException.ThrowIf(_disposed, this); _completed = true; }
        public void RequireRecovery() { ObjectDisposedException.ThrowIf(_disposed, this); owner._recoveryRequired = true; _completed = true; }
        public void Dispose()
        {
            if (_disposed) return;
            lock (owner._gate)
            {
                if (!_completed) Volatile.Write(ref owner._current, new DataRootPaths(previous));
                owner._processLease?.Dispose(); owner._processLease = null;
                owner._pending = false;
                _disposed = true;
            }
            owner._maintenanceSerial.Release();
        }
    }
}
