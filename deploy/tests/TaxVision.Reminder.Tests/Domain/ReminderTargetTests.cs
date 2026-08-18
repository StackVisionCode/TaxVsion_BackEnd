using TaxVision.Reminder.Domain.ValueObjects;

namespace TaxVision.Reminder.Tests.Domain;

public sealed class ReminderTargetTests
{
    [Fact]
    public void General_ConTargetId_Falla()
    {
        // T1 — un General con targetId significa que el publicador se equivocó de categoría, y
        // guardarlo dejaría un ID que nadie puede resolver.
        var result = ReminderTarget.Create(ReminderCategory.General, Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal("Reminder.Target.UnexpectedTarget", result.Error.Code);
    }

    [Fact]
    public void General_SinTargetId_EsValido()
    {
        var result = ReminderTarget.Create(ReminderCategory.General, targetId: null);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.TargetId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void NoGeneral_SinTargetUtil_Falla(string? targetId)
    {
        // T2 — Guid.Empty no es un objetivo, aunque técnicamente sea un Guid.
        var result = ReminderTarget.Create(ReminderCategory.Task, targetId is null ? null : Guid.Parse(targetId));

        Assert.True(result.IsFailure);
        Assert.Equal("Reminder.Target.TargetRequired", result.Error.Code);
    }
}
