using TaxVision.Tasks.Domain.Labels;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Tests.Labels;

public sealed class TaskLabelTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Theory]
    [InlineData("waiting_docs")]
    [InlineData("WAITING_DOCS")]
    [InlineData("  waiting_docs  ")]
    public void The_code_is_normalized_to_lowercase_and_trimmed(string raw)
    {
        var code = TaskLabelCode.Create(raw);

        Assert.Equal("waiting_docs", code.Value.Value);
    }

    [Theory]
    [InlineData("waiting docs")]
    [InlineData("waiting-docs")]
    [InlineData("_waiting")]
    [InlineData("waiting__docs")]
    public void A_code_that_is_not_a_slug_is_rejected(string raw)
    {
        Assert.Equal(TaskErrors.Label.CodeInvalid, TaskLabelCode.Create(raw).Error);
    }

    [Theory]
    [InlineData("#2e7d32", "#2E7D32")]
    [InlineData("#FFFFFF", "#FFFFFF")]
    public void The_color_is_normalized_to_uppercase(string raw, string expected)
    {
        Assert.Equal(expected, LabelColor.Create(raw).Value.Value);
    }

    [Theory]
    [InlineData("2E7D32")]
    [InlineData("#2E7D3")]
    [InlineData("#GGGGGG")]
    public void A_color_that_is_not_six_hex_digits_is_rejected(string raw)
    {
        Assert.Equal(TaskErrors.Label.ColorInvalid, LabelColor.Create(raw).Error);
    }

    /// <summary>
    /// El label es presentación: cambia el nombre visible y el estado al que apunta, pero el motor
    /// sigue leyendo <c>TaskItemStatus</c>.
    /// </summary>
    [Fact]
    public void Renaming_keeps_the_code_and_changes_the_rest()
    {
        var label = NewLabel();

        label.Rename("Esperando documentos", LabelColor.Create("#B71C1C").Value, TaskItemStatus.WaitingOnClient, 3);

        Assert.Equal("waiting_docs", label.Code.Value);
        Assert.Equal("Esperando documentos", label.DisplayName);
        Assert.Equal("#B71C1C", label.Color.Value);
        Assert.Equal(TaskItemStatus.WaitingOnClient, label.MapsToStatus);
        Assert.Equal(3, label.SortOrder);
    }

    [Fact]
    public void A_label_without_display_name_is_rejected()
    {
        var created = TaskLabel.Create(
            TenantId,
            TaskLabelCode.Create("waiting_docs").Value,
            "   ",
            LabelColor.Create("#2E7D32").Value,
            TaskItemStatus.NotStarted,
            0
        );

        Assert.Equal(TaskErrors.Label.DisplayNameEmpty, created.Error);
    }

    private static TaskLabel NewLabel() =>
        TaskLabel
            .Create(
                TenantId,
                TaskLabelCode.Create("waiting_docs").Value,
                "Pendiente",
                LabelColor.Create("#2E7D32").Value,
                TaskItemStatus.NotStarted,
                1
            )
            .Value;
}
