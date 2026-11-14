using System.Net;
using System.Text;
using System.Text.Json;
using BuildingBlocks.Infrastructure.Security;
using ClosedXML.Excel;
using Microsoft.Extensions.Options;
using TaxVision.FinancialInsights.Infrastructure.Processing;
using Xunit;

namespace TaxVision.FinancialInsights.Tests;

public sealed class FinancialProcessingTests
{
    [Fact]
    public async Task Csv_and_xlsx_are_extracted_locally_without_images()
    {
        var extractor = new LocalStatementExtractor(Options.Create(new FinancialProcessingOptions()));
        var csv = await extractor.ExtractAsync(Encoding.UTF8.GetBytes("date,description,amount\n2026-01-01,Deposit,100"),
            "text/csv", CancellationToken.None);
        Assert.Single(csv);
        Assert.Null(csv[0].ImageBytes);
        Assert.Contains("Deposit", csv[0].Text);

        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Checking");
        sheet.Cell("A1").Value = "Merchant";
        sheet.Cell("B1").Value = "25.00";
        using var data = new MemoryStream();
        workbook.SaveAs(data);
        var xlsx = await extractor.ExtractAsync(data.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", CancellationToken.None);
        Assert.Contains("Checking", xlsx[0].Text);
        Assert.Contains("Merchant", xlsx[0].Text);
        Assert.Null(xlsx[0].ImageBytes);
    }

    [Fact]
    public async Task OpenAi_request_sends_only_text_until_a_page_needs_image_fallback()
    {
        var sent = new List<string>();
        var handler = new StubHandler(async request =>
        {
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            sent.Add(await request.Content!.ReadAsStringAsync());
            return JsonResponse(new
            {
                status = "completed",
                output = new[] { new { type = "message", content = new[] { new { type = "output_text",
                    text = "{\"movements\":[],\"needsReview\":true}" } } } },
                usage = new { input_tokens = 120, output_tokens = 40 }
            });
        });
        var client = new OpenAiStatementClient(new HttpClient(handler)
            { BaseAddress = new Uri("https://api.openai.com/v1/") },
            Options.Create(new OpenAiFinancialOptions { ApiKey = "test-only", Model = "gpt-5.4-mini" }));

        var text = await client.AnalyzeAsync([new ExtractedPage(1, "statement text")], CancellationToken.None);
        Assert.True(text.NeedsReview);
        Assert.Equal(120, text.InputTokens);
        Assert.DoesNotContain("input_image", sent[0]);
        Assert.Contains("\"store\":false", sent[0]);
        Assert.Contains("\"json_schema\"", sent[0]);

        await client.AnalyzeAsync([new ExtractedPage(1, "poor OCR", [1, 2, 3], "image/png")], CancellationToken.None);
        Assert.Contains("data:image/png;base64,AQID", sent[1]);
        Assert.DoesNotContain("test-only", sent[1]);
    }

    [Fact]
    public async Task Storage_refuses_unscanned_file_before_requesting_a_signed_url()
    {
        var requestedPaths = new List<string>();
        var fileId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var handler = new StubHandler(request =>
        {
            requestedPaths.Add(request.RequestUri!.AbsolutePath);
            return Task.FromResult(JsonResponse(new
            {
                id = fileId, ownerType = "Customer", ownerId = customerId,
                status = "PendingScan", detectedContentType = "application/pdf", sizeBytes = 100
            }));
        });
        var source = new CloudStorageStatementSource(new HttpClient(handler)
            { BaseAddress = new Uri("http://localhost:5330/") }, new StubTokens(),
            Options.Create(new FinancialProcessingOptions()));
        var error = await Assert.ThrowsAsync<DocumentProcessingException>(() =>
            source.DownloadAsync(Guid.NewGuid(), customerId, fileId, CancellationToken.None));
        Assert.Equal("StorageEligibility", error.Code);
        Assert.Single(requestedPaths);
    }

    [Fact]
    public async Task Storage_rejects_signed_url_for_unapproved_port()
    {
        var fileId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var requests = 0;
        var handler = new StubHandler(_ =>
        {
            requests++;
            return Task.FromResult(requests == 1
                ? JsonResponse(new { id = fileId, ownerType = "Customer", ownerId = customerId,
                    status = "Available", detectedContentType = "application/pdf", sizeBytes = 100 })
                : JsonResponse(new { fileId, downloadUrl = "http://localhost:5124/internal/secret",
                    expiresAtUtc = DateTime.UtcNow.AddMinutes(5) }));
        });
        var source = new CloudStorageStatementSource(new HttpClient(handler)
            { BaseAddress = new Uri("http://localhost:5330/") }, new StubTokens(),
            Options.Create(new FinancialProcessingOptions()));
        var error = await Assert.ThrowsAsync<DocumentProcessingException>(() =>
            source.DownloadAsync(Guid.NewGuid(), customerId, fileId, CancellationToken.None));
        Assert.Equal("StorageDownloadUrl", error.Code);
        Assert.Equal(2, requests);
    }

    private static HttpResponseMessage JsonResponse(object data) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(data), Encoding.UTF8, "application/json")
    };

    private sealed class StubTokens : IServiceTokenAcquirer
    {
        public Task<string?> GetTokenAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<string?>("service-test-token");
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => send(request);
    }
}
