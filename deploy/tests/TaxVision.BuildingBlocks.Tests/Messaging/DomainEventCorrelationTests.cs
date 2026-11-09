using BuildingBlocks.Common;
using BuildingBlocks.Domain;
using BuildingBlocks.Web.Common;
using BuildingBlocks.Web.Hosting;
using BuildingBlocks.Web.Middleware;
using JasperFx.CodeGeneration.Model;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Messaging;

/// <summary>
/// BB-9: con <c>UseTaxVisionDomainEvents()</c>, el aviso que devuelve un handler de dominio lleva el
/// <c>CorrelationId</c> del origen, tanto en el contrato como en el sobre.
/// </summary>
public sealed class DomainEventCorrelationTests : IAsyncLifetime
{
    private const string RequestCorrelationId = "req-bb9-origin";

    private readonly CorrelationProbeRecorder _recorder = new();
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddScoped<CorrelationContext>();
        builder.Services.AddScoped<ICorrelationContext>(sp => sp.GetRequiredService<CorrelationContext>());
        builder.Services.AddSingleton(_recorder);
        builder.Host.UseWolverine(options =>
        {
            options.ServiceName = "bb9-correlation-test";
            options.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;
            options
                .Discovery.DisableConventionalDiscovery()
                .IncludeType(typeof(CorrelationProbeCommandHandler))
                .IncludeType(typeof(CorrelationProbeDomainEventHandler))
                .IncludeType(typeof(CorrelationProbeNoticeHandler));
            options.UseTaxVisionDomainEvents();
            // Sin base: la durabilidad de la cola se prueba en las T-INT de BB-1.
            options.LocalQueue(DomainEventDispatchExtensions.DomainEventsQueue).BufferedInMemory();
        });

        _app = builder.Build();
        _app.UseMiddleware<CorrelationIdMiddleware>();
        _app.MapPost(
            "/invoke",
            async (IMessageBus bus) =>
            {
                await bus.InvokeAsync(new CorrelationProbeCommand());
                return Microsoft.AspNetCore.Http.Results.Ok();
            }
        );
        _app.MapPost(
            "/publish",
            async (IMessageBus bus) =>
            {
                await bus.PublishAsync(new CorrelationProbeCommand());
                return Microsoft.AspNetCore.Http.Results.Ok();
            }
        );
        await _app.StartAsync();

        var address = _app
            .Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First();
        _client = new HttpClient { BaseAddress = new Uri(address) };
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    [Theory]
    [InlineData("/invoke")]
    [InlineData("/publish")]
    public async Task NoticeFromDomainHandler_CarriesRequestCorrelationId(string route)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, route);
        request.Headers.Add(CorrelationIdMiddleware.Header, RequestCorrelationId);

        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var notice = await _recorder.WaitForNoticeAsync();
        Assert.Equal(RequestCorrelationId, notice.ContractCorrelationId);
        Assert.Equal(RequestCorrelationId, notice.EnvelopeCorrelationId);
    }

    [Fact]
    public async Task NoticeFromDomainHandler_CarriesCorrelationIdOfIncomingMessage()
    {
        // Un consumidor: el sobre ya trae el id y en el scope del handler no hay contexto.
        await using var scope = _app.Services.CreateAsyncScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageContext>();
        bus.CorrelationId = "msg-bb9-origin";

        await bus.PublishAsync(new CorrelationProbeDomainEvent());

        var notice = await _recorder.WaitForNoticeAsync();
        Assert.Equal("msg-bb9-origin", notice.ContractCorrelationId);
        Assert.Equal("msg-bb9-origin", notice.EnvelopeCorrelationId);
    }
}

public sealed record CorrelationProbeCommand;

public sealed record CorrelationProbeDomainEvent : IDomainEvent;

public sealed record CorrelationProbeNotice(string ContractCorrelationId);

public sealed record RecordedCorrelationNotice(string ContractCorrelationId, string? EnvelopeCorrelationId);

public sealed class CorrelationProbeRecorder
{
    private readonly TaskCompletionSource<RecordedCorrelationNotice> _notice = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );

    public void Record(RecordedCorrelationNotice notice) => _notice.TrySetResult(notice);

    public Task<RecordedCorrelationNotice> WaitForNoticeAsync() => _notice.Task.WaitAsync(TimeSpan.FromSeconds(30));
}

public static class CorrelationProbeCommandHandler
{
    public static CorrelationProbeDomainEvent Handle(CorrelationProbeCommand _) => new();
}

public static class CorrelationProbeDomainEventHandler
{
    public static CorrelationProbeNotice Handle(CorrelationProbeDomainEvent _, ICorrelationContext correlation) =>
        new(correlation.CorrelationId);
}

public static class CorrelationProbeNoticeHandler
{
    public static void Handle(CorrelationProbeNotice notice, Envelope envelope, CorrelationProbeRecorder recorder) =>
        recorder.Record(new RecordedCorrelationNotice(notice.ContractCorrelationId, envelope.CorrelationId));
}
