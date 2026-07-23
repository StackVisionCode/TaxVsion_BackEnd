using BuildingBlocks.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Middleware;

public sealed class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        try
        {
            await next(ctx);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "An unhandled exception occurred while processing the request in {Path}",
                ctx.Request.Path
            );

            var (status, title, detail, code) = ex switch
            {
                ConflictException conflict => (
                    StatusCodes.Status409Conflict,
                    "Conflict",
                    conflict.Message,
                    conflict.Code
                ),
                // RBAC Fase 7 — ProjectionPermissionsSource lanza esto cuando el perm_v del JWT
                // quedó atrás de la proyección local: el frontend debe refrescar el token y
                // reintentar, no es un error del servidor.
                UnauthorizedAccessException staleToken => (
                    StatusCodes.Status401Unauthorized,
                    "Unauthorized",
                    "Your session's permissions are out of date. Refresh your token and try again.",
                    staleToken.Message
                ),
                _ => (
                    StatusCodes.Status500InternalServerError,
                    "Internal Server Error",
                    "An unexpected error occurred while processing your request. Use the Correlation ID to report the issue.",
                    "Server.Unexpected"
                ),
            };

            ctx.Response.StatusCode = status;
            await ctx.Response.WriteAsJsonAsync(
                new ProblemDetails
                {
                    Status = status,
                    Title = title,
                    Detail = detail,
                    Extensions =
                    {
                        ["code"] = code,
                        ["correlationId"] =
                            ctx.Response.Headers[CorrelationIdMiddleware.Header].FirstOrDefault()
                            ?? ctx.Request.Headers[CorrelationIdMiddleware.Header].FirstOrDefault(),
                    },
                }
            );
        }
    }
}
