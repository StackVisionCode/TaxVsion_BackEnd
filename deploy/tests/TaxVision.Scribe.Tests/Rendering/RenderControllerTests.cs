using System.Text.Json;
using BuildingBlocks.Results;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Scribe.Api.Controllers;
using TaxVision.Scribe.Application.Rendering;

namespace TaxVision.Scribe.Tests.Rendering;

/// <summary>SCR-11: lo que Notification lee para decidir. 4xx con código tipado es determinista; 5xx se reintenta.</summary>
public sealed class RenderControllerTests
{
    [Theory]
    [InlineData("EmailRenderer.NoMapping", 404)]
    [InlineData("EmailRenderer.NoPublishedVersion", 404)]
    [InlineData("EmailRenderer.LayoutVersionNotFound", 404)]
    [InlineData("EmailTemplate.NotFound", 404)]
    [InlineData("EmailRenderer.Parse", 400)]
    [InlineData("EmailRenderer.Render", 400)]
    public async Task Deterministic_errors_are_typed_4xx(string code, int expectedStatus)
    {
        var result = await RenderFailingWith(code);

        Assert.Equal(expectedStatus, result.StatusCode);
        var body = JsonSerializer.Serialize(result.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(code, JsonDocument.Parse(body).RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Render_timeout_is_a_transient_503()
    {
        var result = await RenderFailingWith("EmailRenderer.Timeout");

        Assert.Equal(503, result.StatusCode);
    }

    private static async Task<ObjectResult> RenderFailingWith(string code)
    {
        var bus = new FakeMessageBus
        {
            OnInvoke = _ => Task.FromResult<object?>(Result.Failure<RenderedContent>(new Error(code, "Failed."))),
        };
        var controller = new RenderController(bus);

        var result = await controller.Render(
            new RenderController.RenderHttpRequest("auth.welcome.v1", null, null, new Dictionary<string, object?>()),
            CancellationToken.None
        );

        return Assert.IsType<ObjectResult>(result);
    }
}
