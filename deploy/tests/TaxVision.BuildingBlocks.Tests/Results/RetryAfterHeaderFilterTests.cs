using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Results;

public sealed class RetryAfterHeaderFilterTests
{
    [Fact]
    public void Throttled_error_result_gets_the_Retry_After_header()
    {
        var error = new Error("Test.Throttled", "Wait.").WithRetryAfter(TimeSpan.FromSeconds(58));
        var context = Context(new ObjectResult(error) { StatusCode = StatusCodes.Status429TooManyRequests });

        new RetryAfterHeaderFilter().OnResultExecuting(context);

        Assert.Equal("58", context.HttpContext.Response.Headers.RetryAfter.ToString());
    }

    [Fact]
    public void Error_without_a_wait_leaves_the_response_untouched()
    {
        var context = Context(new ObjectResult(new Error("Test.Failed", "Nope.")) { StatusCode = 400 });

        new RetryAfterHeaderFilter().OnResultExecuting(context);

        Assert.False(context.HttpContext.Response.Headers.ContainsKey("Retry-After"));
    }

    private static ResultExecutingContext Context(IActionResult result) =>
        new(
            new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()),
            [],
            result,
            controller: new object()
        );
}
