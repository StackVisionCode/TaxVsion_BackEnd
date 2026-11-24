using TaxVision.Tasks.Domain.Statuses;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Tests.Labels;

/// <summary>Estados de la oficina (doc 03 §5): el nombre y el color no mandan; la categoría sí.</summary>
public sealed class WorkflowStatusTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Admin = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);
    private static readonly LabelColor Blue = LabelColor.Create("#2563EB").Value;

    [Theory]
    [InlineData("Under review", "under_review")]
    [InlineData("  Ready to   file! ", "ready_to_file")]
    [InlineData("Señal del IRS", "senal_del_irs")]
    [InlineData("!!!", "status")]
    public void The_code_is_generated_from_the_name(string name, string expected)
    {
        Assert.Equal(expected, TaskLabelCode.FromDisplayName(name, new HashSet<string>()).Value);
    }

    [Fact]
    public void A_generated_code_never_repeats_one_in_use_nor_a_system_code()
    {
        var taken = new HashSet<string> { "under_review", "under_review_2" };

        Assert.Equal("under_review_3", TaskLabelCode.FromDisplayName("Under review", taken).Value);
        Assert.Equal("completed_2", TaskLabelCode.FromDisplayName("Completed", new HashSet<string>()).Value);
    }

    [Fact]
    public void A_long_name_still_gives_a_valid_code()
    {
        var code = TaskLabelCode.FromDisplayName(new string('a', 80), new HashSet<string>());

        Assert.True(code.Value.Length <= TaskLabelCode.MaxLength);
    }

    [Fact]
    public void The_five_system_codes_are_reserved()
    {
        Assert.Equal("waiting_on_client", TaskLabelCode.SystemCodeOf(TaskItemStatus.WaitingOnClient).Value);
        Assert.True(TaskLabelCode.IsReserved(TaskLabelCode.SystemCodeOf(TaskItemStatus.Cancelled)));
    }

    [Fact]
    public void A_new_status_goes_in_an_open_category()
    {
        var created = NewStatus("Filed", TaskItemStatus.Completed);

        Assert.Equal(TaskErrors.Status.CategoryLocked, created.Error);
    }

    [Fact]
    public void A_new_status_keeps_who_created_it()
    {
        var status = NewStatus("Under review", TaskItemStatus.InProgress).Value;

        Assert.Equal(TaskItemStatus.InProgress, status.Category);
        Assert.Equal(Admin, status.CreatedByUserId);
        Assert.Equal(Now, status.CreatedAtUtc);
        Assert.False(status.IsDefault);
        Assert.False(status.IsArchived);
        Assert.False(status.IsSystem);
    }

    [Fact]
    public void The_description_has_a_limit()
    {
        var status = NewStatus("Under review", TaskItemStatus.InProgress).Value;

        var result = status.Describe(new string('x', WorkflowStatus.DescriptionMaxLength + 1), Admin, Now);

        Assert.Equal(TaskErrors.Status.DescriptionTooLong, result.Error);
    }

    [Fact]
    public void Archiving_takes_away_the_default_and_restoring_brings_it_back_without_it()
    {
        var status = NewStatus("Under review", TaskItemStatus.InProgress).Value;
        status.MakeDefault();

        status.Archive(Admin, Now);
        Assert.True(status.IsArchived);
        Assert.False(status.IsDefault);
        Assert.Equal(Now, status.ArchivedAtUtc);

        status.Restore(Admin, Now);
        Assert.False(status.IsArchived);
        Assert.Null(status.ArchivedAtUtc);
        Assert.False(status.IsDefault);
    }

    [Fact]
    public void A_system_status_cannot_be_archived()
    {
        var system = WorkflowStatus
            .CustomizeSystem(TenantId, TaskItemStatus.Completed, "Done", Blue, true, Admin, Now)
            .Value;

        Assert.True(system.IsSystem);
        Assert.Equal(TaskErrors.Status.SystemStatus, system.Archive(Admin, Now).Error);
    }

    [Fact]
    public void An_archived_status_cannot_become_the_default()
    {
        var status = NewStatus("Under review", TaskItemStatus.InProgress).Value;
        status.Archive(Admin, Now);

        Assert.Equal(TaskErrors.Status.Archived, status.MakeDefault().Error);
    }

    private static BuildingBlocks.Results.Result<WorkflowStatus> NewStatus(string name, TaskItemStatus category) =>
        WorkflowStatus.CreateCustom(
            TenantId,
            TaskLabelCode.FromDisplayName(name, new HashSet<string>()),
            name,
            Blue,
            category,
            10,
            null,
            Admin,
            Now
        );
}
