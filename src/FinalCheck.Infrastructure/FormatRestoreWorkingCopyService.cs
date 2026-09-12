using System.Security.Cryptography;
using FinalCheck.Core.Abstractions;
using FinalCheck.Core.Documents;
using FinalCheck.Core.Formatting;

namespace FinalCheck.Infrastructure;

/// <summary>One managed working file per caller-supplied ContractVersion identity. Never edits the source.</summary>
public sealed class FormatRestoreWorkingCopyService(IAppDataPathProvider paths, IDocumentParser parser,
    IFormatRestoreRenderer renderer, IFormatRestoreStore store, IWorkingCopyComparisonService comparisons) : IFormatRestoreWorkingCopyService
{
    public Task<FormatRestoreResult> ExecuteAsync(Guid contractVersionId, string originalPath, FormatRestorePlan plan,
        FormatRestoreScope? scope = null, IProgress<FormatRestoreProgress>? progress = null, CancellationToken cancellationToken = default) =>
        LockedAsync(contractVersionId, async directory =>
        {
            var recovery = await RecoverCoreAsync(contractVersionId, directory, cancellationToken);
            if (recovery?.Status == FormatRestoreResultStatus.RecoveryNeedsReview) return recovery;
            var existing = await store.LoadWorkingCopyAsync(contractVersionId, cancellationToken);
            var original = ValidateOriginal(originalPath, directory);
            if (existing is not null && existing.OriginalPath != original) return Failure("OriginalIdentityMismatch", existing);
            var workingPath = Path.Combine(directory, "restored.docx");
            if (existing is null && File.Exists(workingPath)) return Failure("UntrackedWorkingCopy", null);
            if (existing is not null)
            {
                ValidateMetadataPath(existing, directory);
                if (!File.Exists(workingPath) || await HashAsync(workingPath, cancellationToken) != existing.Sha256) return External(existing);
            }
            var sourcePath = existing?.WorkingPath ?? original;
            FormatRestoreRenderResult rendered;
            await using (var source = OpenRead(sourcePath))
                rendered = await renderer.RenderAsync(source, plan, scope, progress, cancellationToken);
            if (rendered.Mutations.Count == 0) return new(FormatRestoreResultStatus.NoChanges, existing, null, plan.Diagnostics.Concat(rendered.Diagnostics).ToArray());
            var originalHash = existing?.OriginalSha256 ?? plan.SourceSha256;
            return await PublishAsync(contractVersionId, original, originalHash, existing, directory, rendered,
                FormatRestoreOperationKind.Restore, plan, scope ?? new(), null, progress, cancellationToken);
        }, cancellationToken);

    public Task<FormatRestoreResult> UndoLastAsync(Guid contractVersionId, CancellationToken cancellationToken = default) =>
        LockedAsync(contractVersionId, async directory =>
        {
            var recovery = await RecoverCoreAsync(contractVersionId, directory, cancellationToken);
            if (recovery?.Status == FormatRestoreResultStatus.RecoveryNeedsReview) return recovery;
            var copy = await store.LoadWorkingCopyAsync(contractVersionId, cancellationToken);
            if (copy is null) return Failure("NoWorkingCopy", null);
            ValidateMetadataPath(copy, directory);
            var last = await store.LoadLastOperationAsync(contractVersionId, cancellationToken);
            if (last is null || last.Kind != FormatRestoreOperationKind.Restore || last.Status != FormatRestoreOperationStatus.Completed)
                return Failure("NoUndoableRestore", copy);
            if (!File.Exists(copy.WorkingPath) || await HashAsync(copy.WorkingPath, cancellationToken) != last.AfterSha256)
                return new(FormatRestoreResultStatus.WorkingCopyExternallyModified, copy, null, [new("UndoHashMismatch", null, "External edits prevent format-only Undo.")]);
            FormatRestoreRenderResult rendered;
            await using (var source = OpenRead(copy.WorkingPath)) rendered = await renderer.RevertAsync(source, last.AfterSha256, last.Mutations, cancellationToken);
            return await PublishAsync(contractVersionId, copy.OriginalPath, copy.OriginalSha256, copy, directory, rendered,
                FormatRestoreOperationKind.Undo, last.Plan, last.Scope, last.OperationId, null, cancellationToken);
        }, cancellationToken);

    public Task<FormatRestoreResult> PreserveExternalChangesAsync(Guid contractVersionId, DocumentSnapshot baseline, CancellationToken cancellationToken = default) =>
        LockedAsync(contractVersionId, async directory =>
        {
            var recovery = await RecoverCoreAsync(contractVersionId, directory, cancellationToken);
            if (recovery?.Status == FormatRestoreResultStatus.RecoveryNeedsReview) return recovery;
            var copy = await store.LoadWorkingCopyAsync(contractVersionId, cancellationToken);
            if (copy is null) return Failure("NoWorkingCopy", null);
            ValidateMetadataPath(copy, directory);
            DocumentSnapshot snapshot;
            await using (var source = OpenRead(copy.WorkingPath)) snapshot = await parser.ParseAsync(source, cancellationToken: cancellationToken);
            var comparison = comparisons.Compare(baseline, snapshot, cancellationToken);
            var operationId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            var updated = copy with { Sha256 = snapshot.Metadata.Sha256, Snapshot = snapshot, UpdatedAt = now, LastOperationId = operationId };
            var operation = new FormatRestoreOperation(FormatRestoreOperation.CurrentSchemaVersion, operationId, contractVersionId,
                FormatRestoreOperationKind.PreserveExternalChanges, FormatRestoreOperationStatus.Prepared, null, new(), now, null,
                snapshot.Metadata.Sha256, snapshot.Metadata.Sha256, [], [new("ExternalModificationDetected", null, "Explicitly accepted; new Snapshot and Comparison are appended.")],
                updated, "", "", RebuiltComparison: comparison, PreviousMetadataSha256: copy.Sha256);
            await store.SavePreparedAsync(operation, cancellationToken);
            if (await HashAsync(copy.WorkingPath, cancellationToken) != snapshot.Metadata.Sha256)
            {
                await store.FailAsync(operation, CancellationToken.None);
                return External(copy);
            }
            await store.CompleteAsync(operation, CancellationToken.None);
            return Success(operation);
        }, cancellationToken);

    public Task<FormatRestoreResult> RegenerateAsync(Guid contractVersionId, CancellationToken cancellationToken = default) =>
        LockedAsync(contractVersionId, async directory =>
        {
            var recovery = await RecoverCoreAsync(contractVersionId, directory, cancellationToken);
            if (recovery?.Status == FormatRestoreResultStatus.RecoveryNeedsReview) return recovery;
            var copy = await store.LoadWorkingCopyAsync(contractVersionId, cancellationToken);
            if (copy is null) return Failure("NoWorkingCopy", null);
            ValidateMetadataPath(copy, directory);
            var original = ValidateOriginal(copy.OriginalPath, directory);
            byte[] bytes;
            DocumentSnapshot snapshot;
            await using (var source = OpenRead(original))
            {
                snapshot = await parser.ParseAsync(source, cancellationToken: cancellationToken);
                source.Position = 0;
                using var buffer = new MemoryStream();
                await source.CopyToAsync(buffer, cancellationToken); bytes = buffer.ToArray();
            }
            return await PublishAsync(contractVersionId, original, snapshot.Metadata.Sha256, copy, directory, new(bytes, snapshot, [], []),
                FormatRestoreOperationKind.Regenerate, null, new(), null, null, cancellationToken);
        }, cancellationToken);

    public Task<FormatRestoreResult> RecoverAsync(Guid contractVersionId, CancellationToken cancellationToken = default) =>
        LockedAsync(contractVersionId, async directory => await RecoverCoreAsync(contractVersionId, directory, cancellationToken) ??
            new(FormatRestoreResultStatus.NoChanges, await store.LoadWorkingCopyAsync(contractVersionId, cancellationToken), null, []), cancellationToken);

    private async Task<FormatRestoreResult> PublishAsync(Guid versionId, string originalPath, string originalHash,
        RestoredWorkingCopy? previous, string directory, FormatRestoreRenderResult rendered, FormatRestoreOperationKind kind,
        FormatRestorePlan? plan, FormatRestoreScope scope, Guid? undoesId, IProgress<FormatRestoreProgress>? progress, CancellationToken token)
    {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var target = Path.Combine(directory, "restored.docx");
        var temporary = Path.Combine(directory, $"candidate-{id:N}.docx");
        var backup = Path.Combine(directory, $"previous-{id:N}.docx");
        var beforeHash = File.Exists(target) ? await HashAsync(target, token) : null;
        if (kind != FormatRestoreOperationKind.Regenerate && beforeHash != previous?.Sha256) return External(previous);
        var afterHash = Convert.ToHexString(SHA256.HashData(rendered.DocumentBytes)).ToLowerInvariant();
        var metadata = new RestoredWorkingCopy(versionId, originalPath, originalHash, target, afterHash, rendered.Snapshot, now, id);
        var operation = new FormatRestoreOperation(FormatRestoreOperation.CurrentSchemaVersion, id, versionId, kind, FormatRestoreOperationStatus.Prepared,
            plan, scope, now, null, beforeHash, afterHash, rendered.Mutations, (plan?.Diagnostics ?? []).Concat(rendered.Diagnostics).ToArray(),
            metadata, temporary, backup, undoesId, PreviousMetadataSha256: previous?.Sha256);
        var prepared = false;
        var promoted = false;
        try
        {
            progress?.Report(new(FormatRestoreStage.Saving, 0, null));
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.WriteThrough | FileOptions.Asynchronous))
            {
                await output.WriteAsync(rendered.DocumentBytes, token);
                await output.FlushAsync(token);
                output.Flush(true);
            }
            progress?.Report(new(FormatRestoreStage.Reparsing, 0, null));
            var reparsed = await parser.ParseFileAsync(temporary, cancellationToken: token);
            if (reparsed.Metadata.Sha256 != afterHash) throw new InvalidDataException("ReparseValidationFailed.");
            progress?.Report(new(FormatRestoreStage.Validating, 0, null));
            await store.SavePreparedAsync(operation, token); prepared = true;
            token.ThrowIfCancellationRequested();
            if ((File.Exists(target) ? await HashAsync(target, token) : null) != beforeHash)
                throw new InvalidDataException("ExternalModificationDetected during publication.");
            if ((previous is null || kind == FormatRestoreOperationKind.Regenerate) && await HashAsync(originalPath, token) != originalHash)
                throw new InvalidDataException("Original source changed during restore.");
            // No cancellation inside the publication + database completion critical section.
            if (File.Exists(target)) File.Replace(temporary, target, backup); else File.Move(temporary, target, false);
            promoted = true;
            await store.CompleteAsync(operation, CancellationToken.None);
            try { progress?.Report(new(FormatRestoreStage.Completed, rendered.Mutations.Count, rendered.Mutations.Count)); }
            catch (Exception) { /* Observer errors cannot roll back a committed file/database operation. */ }
            return Success(operation);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or OperationCanceledException || prepared)
        {
            if (promoted)
            {
                try
                {
                    var committed = await store.LoadWorkingCopyAsync(versionId, CancellationToken.None);
                    if (committed?.LastOperationId == id && committed.Sha256 == afterHash) return Success(operation);
                }
                catch (Exception)
                {
                    return new(FormatRestoreResultStatus.RecoveryNeedsReview, previous, operation,
                        [new("OperationPersistenceUncertain", null, "Validated file, backup and durable journal retained; investigate before continuing.")]);
                }
            }
            var rolledBack = !promoted;
            try
            {
                if (promoted && File.Exists(target) && await HashAsync(target, CancellationToken.None) == afterHash)
                {
                    if (File.Exists(backup)) File.Replace(backup, target, null);
                    else File.Move(target, Path.Combine(directory, $"recovery-{id:N}.docx"), false);
                    rolledBack = true;
                }
            }
            catch (Exception rollbackException) when (rollbackException is IOException or UnauthorizedAccessException)
            { return new(FormatRestoreResultStatus.RecoveryNeedsReview, previous, operation, [new("RollbackNeedsReview", null, "Validated working file, backup and journal retained; file access prevented safe rollback.")]); }
            if (prepared && rolledBack)
            {
                try { await store.FailAsync(operation with { Diagnostics = operation.Diagnostics.Append(new("WorkingCopyWriteFailed", null, exception.GetType().Name)).ToArray() }, CancellationToken.None); }
                catch (Exception) { /* Durable Prepared journal is deliberately retained for recovery. */ }
            }
            return new(exception is OperationCanceledException ? FormatRestoreResultStatus.Cancelled : rolledBack ? FormatRestoreResultStatus.Failed : FormatRestoreResultStatus.RecoveryNeedsReview,
                previous, operation, [new("WorkingCopyWriteFailed", null, exception.GetType().Name)]);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } // exact engine-owned unique candidate only
            catch (IOException) { /* A locked orphan candidate is not the authoritative working copy. */ }
            catch (UnauthorizedAccessException) { /* Preserve it rather than masking a committed result. */ }
        }
    }

    private async Task<FormatRestoreResult?> RecoverCoreAsync(Guid versionId, string directory, CancellationToken token)
    {
        var pending = await store.LoadPendingAsync(versionId, token);
        if (pending.Count == 0) return null;
        if (pending.Count != 1) return Recovery("MultiplePendingOperations", null);
        var operation = pending[0];
        ValidateMetadataPath(operation.WorkingCopy, directory);
        if (operation.TemporaryPath.Length > 0 && operation.TemporaryPath != Path.Combine(directory, $"candidate-{operation.OperationId:N}.docx") ||
            operation.BackupPath.Length > 0 && operation.BackupPath != Path.Combine(directory, $"previous-{operation.OperationId:N}.docx"))
            return Recovery("InvalidJournalPaths", operation.WorkingCopy);
        var hash = File.Exists(operation.WorkingCopy.WorkingPath) ? await HashAsync(operation.WorkingCopy.WorkingPath, token) : null;
        if (hash == operation.AfterSha256)
        {
            var snapshot = await parser.ParseFileAsync(operation.WorkingCopy.WorkingPath, cancellationToken: token);
            if (snapshot.Metadata.Sha256 != operation.WorkingCopy.Snapshot.Metadata.Sha256) return Recovery("RecoveryValidationFailed", operation.WorkingCopy);
            await store.CompleteAsync(operation, CancellationToken.None);
            return Success(operation);
        }
        if (hash == operation.BeforeSha256)
        {
            await store.FailAsync(operation, CancellationToken.None);
            return null; // original/previous working file was never promoted or was rolled back
        }
        return Recovery("ExternalModificationDuringRecovery", operation.WorkingCopy);
    }

    private async Task<FormatRestoreResult> LockedAsync(Guid versionId, Func<string, Task<FormatRestoreResult>> action, CancellationToken token)
    {
        try
        {
            if (versionId == Guid.Empty) throw new ArgumentException("A stable ContractVersion identity is required.");
            token.ThrowIfCancellationRequested();
            var root = Path.GetFullPath(Path.Combine(paths.GetAppDataDirectory(), "WorkingCopies"));
            var directory = Path.Combine(root, versionId.ToString("N"));
            RejectLinks(directory);
            Directory.CreateDirectory(directory);
            await using var exclusive = new FileStream(Path.Combine(directory, "operation.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return await action(directory);
        }
        catch (OperationCanceledException) { return new(FormatRestoreResultStatus.Cancelled, null, null, [new("Cancelled", null, "No unvalidated file was published.")]); }
        catch (Exception exception)
        { return Failure("WorkingCopyWriteFailed", null, exception.GetType().Name); }
    }

    private static string ValidateOriginal(string originalPath, string directory)
    {
        var original = Path.GetFullPath(originalPath);
        var managedRoot = Path.GetDirectoryName(directory)!;
        if (original.StartsWith(managedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("An original DOCX cannot be an engine-managed working file.");
        RejectLinks(original);
        if (!File.Exists(original) || !original.EndsWith(".docx", StringComparison.OrdinalIgnoreCase)) throw new IOException("Original DOCX is missing.");
        return original;
    }

    private static void ValidateMetadataPath(RestoredWorkingCopy metadata, string directory)
    {
        if (metadata.WorkingPath != Path.Combine(directory, "restored.docx")) throw new InvalidOperationException("Working copy metadata path is invalid.");
        RejectLinks(metadata.WorkingPath);
    }

    private static void RejectLinks(string path)
    {
        for (var current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Symlink/reparse-point paths are not supported for managed publication.");
    }

    private static FileStream OpenRead(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    private static async Task<string> HashAsync(string path, CancellationToken token)
    {
        await using var stream = OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token)).ToLowerInvariant();
    }
    private static FormatRestoreResult Success(FormatRestoreOperation operation) => new(FormatRestoreResultStatus.Completed, operation.WorkingCopy,
        operation with { Status = FormatRestoreOperationStatus.Completed, CompletedAt = DateTimeOffset.UtcNow }, operation.Diagnostics);
    private static FormatRestoreResult External(RestoredWorkingCopy? copy) => new(FormatRestoreResultStatus.WorkingCopyExternallyModified, copy, null,
        [new("WorkingCopyExternallyModified", null, "Reparse/preserve or explicitly regenerate; never overwrite external edits automatically.")]);
    private static FormatRestoreResult Recovery(string code, RestoredWorkingCopy? copy) => new(FormatRestoreResultStatus.RecoveryNeedsReview, copy, null, [new(code, null, "Pending journal needs explicit investigation; files are preserved.")]);
    private static FormatRestoreResult Failure(string code, RestoredWorkingCopy? copy, string message = "Operation was not completed.") => new(FormatRestoreResultStatus.Failed, copy, null, [new(code, null, message)]);
}
