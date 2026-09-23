using FinalCheck.Core.Comparisons;

namespace FinalCheck.Core.Tests;

public sealed class ComparisonIgnoreProjectionTests
{
    private static ComparisonChangeItem Text(string before, string after, DifferenceSpan span) => new(
        "one", ComparisonChangeKind.TextReplace, "b", "c", "body/p[0]", "body/p[0]", before, after,
        [span], null, [], [], [], ComparisonConfidenceLevel.High, []);

    [Fact] public void PunctuationAndCustomCharactersAreReversibleWithoutMutatingRawSpans()
    {
        var punctuation = Text("甲方，应付款。", "甲方,应付款", new(DifferenceOperation.Replace, 2, 1, 2, 1, "，", ","));
        var policy = new ComparisonIgnoreRules(Punctuation: true);
        Assert.Null(ComparisonIgnoreProjection.Project(punctuation, policy));
        Assert.Single(punctuation.DifferenceSpans);
        Assert.Same(punctuation, ComparisonIgnoreProjection.Project(punctuation, new()));
        var custom = Text("甲※方", "甲方", new(DifferenceOperation.Delete, 1, 1, 1, 0, "※", ""));
        Assert.Null(ComparisonIgnoreProjection.Project(custom, new(Characters: "※")));
        Assert.NotNull(ComparisonIgnoreProjection.Project(custom, new(Characters: "☆")));
    }

    [Fact] public void PageAndNumberingRulesDoNotHideBodyAmountsDatesOrClauseContents()
    {
        var page = Text("第 3 页", "第 4 页", new(DifferenceOperation.Replace, 2, 1, 2, 1, "3", "4"));
        Assert.Null(ComparisonIgnoreProjection.Project(page, new(PageNumbers: true)));
        var money = Text("金额300万元", "金额400万元", new(DifferenceOperation.Replace, 2, 1, 2, 1, "3", "4"));
        var date = Text("2026年9月", "2027年9月", new(DifferenceOperation.Replace, 3, 1, 3, 1, "6", "7"));
        Assert.NotNull(ComparisonIgnoreProjection.Project(money, new(PageNumbers: true, Numbering: true)));
        Assert.NotNull(ComparisonIgnoreProjection.Project(date, new(PageNumbers: true, Numbering: true)));
        var number = Text("1. 付款30日", "2. 付款60日", new(DifferenceOperation.Replace, 0, 1, 0, 1, "1", "2")) with
        {
            DifferenceSpans = [new(DifferenceOperation.Replace, 0, 1, 0, 1, "1", "2"),
                new(DifferenceOperation.Replace, 5, 2, 5, 2, "30", "60")]
        };
        var projected = ComparisonIgnoreProjection.Project(number, new(Numbering: true));
        Assert.NotNull(projected); Assert.Single(projected.DifferenceSpans); Assert.Equal("30", projected.DifferenceSpans[0].OldText);
        Assert.Equal(2, number.DifferenceSpans.Count);
    }

    [Fact] public void FormatPropertiesFilterIndependentlyAndNeverEraseTableStructureOrText()
    {
        var raw = Text("30日", "60日", new(DifferenceOperation.Replace, 0, 2, 0, 2, "30", "60")) with
        {
            FormatDifference = new(FormatDifferenceScope.Character, "b", "c", [new("Font.EastAsia", "宋体", "仿宋"), new("FontSizeHalfPoints", "24", "20")])
        };
        var full = ComparisonIgnoreProjection.Project(raw, new(AllFormatting: true));
        Assert.NotNull(full); Assert.Single(full.DifferenceSpans); Assert.Null(full.FormatDifference); Assert.Equal(2, raw.FormatDifference.Properties.Count);
        var format = raw with { DifferenceSpans = [], FormatDifference = new(FormatDifferenceScope.Paragraph, "b", "c",
            [new("SpacingBefore", "1", "2"), new("LineSpacing", "20", "30"), new("Alignment", "Left", "Center")]) };
        var spacing = ComparisonIgnoreProjection.Project(format, new(FormatProperties: ["Paragraph.SpacingBefore", "Paragraph.LineSpacing"]));
        Assert.NotNull(spacing); Assert.Equal("Alignment", Assert.Single(spacing.FormatDifference!.Properties).Property);
        var fontOnly = ComparisonIgnoreProjection.Project(raw with { DifferenceSpans = [] }, new(FormatProperties: ["Character.Font.EastAsia", "Character.FontSizeHalfPoints"]));
        Assert.Null(fontOnly);
        var table = format with { Kind = ComparisonChangeKind.TableCellChange, FormatDifference = new(FormatDifferenceScope.TableCell, "b", "c",
            [new("ShadingFill", "red", "blue"), new("GridSpan", "1", "2")]) };
        var tableProjected = ComparisonIgnoreProjection.Project(table, new(AllFormatting: true));
        Assert.NotNull(tableProjected); Assert.Equal("GridSpan", Assert.Single(tableProjected.FormatDifference!.Properties).Property);
        Assert.Equal(2, table.FormatDifference.Properties.Count);
    }
}
