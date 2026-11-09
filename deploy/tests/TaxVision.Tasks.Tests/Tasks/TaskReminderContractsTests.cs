using TaxVision.Tasks.Application.Reminders;

namespace TaxVision.Tasks.Tests.Tasks;

// Reminder ignora una clave existente en cualquier estado: cada ciclo de vida lleva la suya.
public sealed class TaskReminderContractsTests
{
    [Fact]
    public void RequestKey_FirstLifecycle_KeepsLegacyFormat()
    {
        var taskId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        Assert.Equal($"task-created:{taskId:N}:{userId:N}", TaskReminderContracts.RequestKey(taskId, userId, 0));
        Assert.Equal($"task-created:{taskId:N}:{userId:N}:r2", TaskReminderContracts.RequestKey(taskId, userId, 2));
    }
}
