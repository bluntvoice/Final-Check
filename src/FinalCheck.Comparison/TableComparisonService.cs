using FinalCheck.Core.Comparisons;
using FinalCheck.Core.Documents;

namespace FinalCheck.Comparison;

public sealed class TableComparisonService(
    ITextDiffService textDiffService,
    IFormatDiffService formatDiffService) : ITableComparisonService
{
    public TableComparisonResult Compare(
        DocumentSnapshot baseline,
        DocumentSnapshot current,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(current);
        var mappings = new List<ComparisonNodeMapping>();
        var changes = new List<ComparisonChangeItem>();
        var diagnostics = new List<ComparisonDiagnostic>();
        var matchedTableCount = Math.Min(baseline.Tables.Count, current.Tables.Count);
        for (var tableIndex = 0; tableIndex < matchedTableCount; tableIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CompareTable(
                baseline.Tables[tableIndex],
                current.Tables[tableIndex],
                mappings,
                changes,
                diagnostics,
                cancellationToken);
        }

        foreach (var table in baseline.Tables.Skip(matchedTableCount))
        {
            changes.Add(TableStructureChange(table, null));
            diagnostics.Add(StructureDiagnostic(table.NodeId, null, "A baseline table has no current structural counterpart."));
        }

        foreach (var table in current.Tables.Skip(matchedTableCount))
        {
            changes.Add(TableStructureChange(null, table));
            diagnostics.Add(StructureDiagnostic(null, table.NodeId, "A current table has no baseline structural counterpart."));
        }

        return new TableComparisonResult(mappings, changes, diagnostics);
    }

    private void CompareTable(
        DocumentTableSnapshot baseline,
        DocumentTableSnapshot current,
        List<ComparisonNodeMapping> mappings,
        List<ComparisonChangeItem> changes,
        List<ComparisonDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        mappings.Add(Mapping(baseline.Identity, baseline.Index, current.Identity, current.Index));
        var tableFormat = formatDiffService.CompareTable(baseline, current);
        if (tableFormat is not null)
        {
            changes.Add(FormatChange(
                $"table-format:{baseline.NodeId}:{current.NodeId}",
                ComparisonChangeKind.TableChange,
                baseline.Identity,
                current.Identity,
                string.Empty,
                string.Empty,
                tableFormat));
        }

        if (baseline.Rows.Count != current.Rows.Count ||
            baseline.Rows.Zip(current.Rows).Any(pair => pair.First.Cells.Count != pair.Second.Cells.Count))
        {
            diagnostics.Add(StructureDiagnostic(
                baseline.NodeId,
                current.NodeId,
                "Table row or cell counts differ; reliable cells are still compared by row and column."));
        }

        var rowCount = Math.Min(baseline.Rows.Count, current.Rows.Count);
        for (var rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var baselineCells = baseline.Rows[rowIndex].Cells.ToDictionary(cell => cell.ColumnIndex);
            var currentCells = current.Rows[rowIndex].Cells.ToDictionary(cell => cell.ColumnIndex);
            foreach (var columnIndex in baselineCells.Keys.Intersect(currentCells.Keys).OrderBy(value => value))
            {
                CompareCell(
                    baselineCells[columnIndex],
                    currentCells[columnIndex],
                    mappings,
                    changes,
                    cancellationToken);
            }
        }
    }

    private void CompareCell(
        DocumentTableCellSnapshot baseline,
        DocumentTableCellSnapshot current,
        List<ComparisonNodeMapping> mappings,
        List<ComparisonChangeItem> changes,
        CancellationToken cancellationToken)
    {
        mappings.Add(Mapping(baseline.Identity, baseline.Index, current.Identity, current.Index));
        var spans = textDiffService.Compare(baseline.DisplayText, current.DisplayText, cancellationToken);
        var format = formatDiffService.CompareCell(baseline, current);
        if (spans.Count == 0 && format is null)
        {
            return;
        }

        changes.Add(new ComparisonChangeItem(
            $"cell:{baseline.NodeId}:{current.NodeId}",
            ComparisonChangeKind.TableCellChange,
            baseline.NodeId,
            current.NodeId,
            baseline.Identity.StructuralPath,
            current.Identity.StructuralPath,
            baseline.DisplayText,
            current.DisplayText,
            spans,
            format,
            [],
            [],
            [ComparisonEvidenceKind.SnapshotDifference],
            ComparisonConfidenceLevel.Exact,
            []));
    }

    private static ComparisonNodeMapping Mapping(
        DocumentNodeIdentitySnapshot baseline,
        int baselinePosition,
        DocumentNodeIdentitySnapshot current,
        int currentPosition) => new(
        new DocumentNodeReference(baseline.NodeId, baseline.Kind, baselinePosition, baseline.StructuralPath),
        new DocumentNodeReference(current.NodeId, current.Kind, currentPosition, current.StructuralPath),
        ComparisonMappingType.Structural,
        ComparisonConfidenceLevel.Exact,
        1,
        [new ComparisonMatchEvidence(ComparisonEvidenceKind.StructuralPath, 1, "Same table or cell structural position.")],
        false,
        false);

    private static ComparisonChangeItem FormatChange(
        string changeId,
        ComparisonChangeKind kind,
        DocumentNodeIdentitySnapshot baseline,
        DocumentNodeIdentitySnapshot current,
        string baselineText,
        string currentText,
        ComparisonFormatDifference format) => new(
        changeId,
        kind,
        baseline.NodeId,
        current.NodeId,
        baseline.StructuralPath,
        current.StructuralPath,
        baselineText,
        currentText,
        [],
        format,
        [],
        [],
        [ComparisonEvidenceKind.SnapshotDifference],
        ComparisonConfidenceLevel.Exact,
        []);

    private static ComparisonChangeItem TableStructureChange(
        DocumentTableSnapshot? baseline,
        DocumentTableSnapshot? current) => new(
        $"table-structure:{baseline?.NodeId ?? "none"}:{current?.NodeId ?? "none"}",
        ComparisonChangeKind.TableChange,
        baseline?.NodeId,
        current?.NodeId,
        baseline?.Identity.StructuralPath,
        current?.Identity.StructuralPath,
        string.Empty,
        string.Empty,
        [],
        null,
        [],
        [],
        [ComparisonEvidenceKind.SnapshotDifference],
        null,
        ["TableStructureChanged"]);

    private static ComparisonDiagnostic StructureDiagnostic(
        string? baselineNodeId,
        string? currentNodeId,
        string message) => new(
        ComparisonDiagnosticSeverity.Warning,
        "TableStructureChanged",
        message,
        baselineNodeId,
        currentNodeId);
}
