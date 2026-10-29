using System.ComponentModel.DataAnnotations;

namespace TaxVision.DataCollection.Engine;

public sealed class DataCollectionLimits
{
    [Range(1024, 5_242_880)]
    public int MaxDefinitionBytes { get; init; } = 524_288;

    [Range(1, 100)]
    public int MaxPages { get; init; } = 50;

    [Range(1, 100)]
    public int MaxSectionsPerPage { get; init; } = 20;

    [Range(1, 200)]
    public int MaxComponentsPerSection { get; init; } = 60;

    [Range(1, 2_000)]
    public int MaxComponents { get; init; } = 600;

    [Range(1, 500)]
    public int MaxOptionsPerChoice { get; init; } = 200;

    [Range(1, 1_000)]
    public int MaxLabelLength { get; init; } = 300;

    [Range(1, 10_000)]
    public int MaxHelpTextLength { get; init; } = 2_000;

    [Range(1, 20_000)]
    public int MaxParagraphLength { get; init; } = 8_000;

    [Range(1, 200)]
    public int MaxConditionNodes { get; init; } = 50;

    [Range(1, 10)]
    public int MaxConditionDepth { get; init; } = 5;

    [Range(1, 200)]
    public int MaxRepeaterRows { get; init; } = 50;

    [Range(1, 1)]
    public int MaxNestingDepth { get; init; } = 1;

    [Range(1024, 10_485_760)]
    public int MaxAnswersBytes { get; init; } = 1_048_576;

    [Range(1, 100)]
    public int MaxFilesPerField { get; init; } = 20;

    [Range(1, 1_000)]
    public int MaxFilesPerSubmission { get; init; } = 200;

    [Range(1, 1_073_741_824)]
    public long MaxSubmissionTotalBytes { get; init; } = 262_144_000;

    [Range(1, 26_214_400)]
    public long MaxFileBytes { get; init; } = 26_214_400;
}
