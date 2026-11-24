using System.Text.Json;
using TaxVision.Tasks.Api.Requests;

namespace TaxVision.Tasks.Tests.Tasks;

// Contrato con el CRM: `completeOpenSubtasks` al completar y `reopenParent` al reabrir (camelCase de MVC).
public sealed class CompleteTaskRequestContractTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public void The_body_the_CRM_sends_binds_complete_open_subtasks()
    {
        var request = JsonSerializer.Deserialize<CompleteTaskRequest>("""{"completeOpenSubtasks":true}""", Json)!;

        Assert.True(request.CompleteOpenSubtasks);
    }

    [Fact]
    public void An_empty_body_keeps_the_old_behaviour()
    {
        var request = JsonSerializer.Deserialize<CompleteTaskRequest>("{}", Json)!;

        Assert.False(request.CompleteOpenSubtasks);
    }

    [Fact]
    public void The_reopen_body_binds_reopen_parent()
    {
        var request = JsonSerializer.Deserialize<ReopenTaskRequest>("""{"reopenParent":true}""", Json)!;

        Assert.True(request.ReopenParent);
    }
}
