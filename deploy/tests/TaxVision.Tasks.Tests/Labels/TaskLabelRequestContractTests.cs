using System.Text.Json;
using System.Text.Json.Serialization;
using TaxVision.Tasks.Api.Requests;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Tests.Labels;

// Contrato con el CRM: el body de POST/PUT /tasks/labels se lee con las mismas opciones que la API
// (camelCase de MVC + enums como texto, Program.cs). El color llegó null mientras el CRM lo mandaba como `labelColor`.
public sealed class TaskLabelRequestContractTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public void The_body_the_CRM_sends_binds_color_status_and_sort_order()
    {
        const string body =
            """{"code":"ready","displayName":"Ready to file","mapsToStatus":"InProgress","color":"#5b5bd6","sortOrder":4}""";

        var request = JsonSerializer.Deserialize<UpsertTaskLabelRequest>(body, Json)!;

        Assert.Equal("ready", request.Code);
        Assert.Equal("Ready to file", request.DisplayName);
        Assert.Equal("#5b5bd6", request.Color);
        Assert.Equal(TaskItemStatus.InProgress, request.MapsToStatus);
        Assert.Equal(4, request.SortOrder);
    }

    [Fact]
    public void A_color_sent_under_another_name_does_not_bind()
    {
        const string body =
            """{"code":"ready","displayName":"Ready to file","mapsToStatus":"InProgress","labelColor":"#5b5bd6"}""";

        var request = JsonSerializer.Deserialize<UpsertTaskLabelRequest>(body, Json)!;

        Assert.Null(request.Color);
    }
}
