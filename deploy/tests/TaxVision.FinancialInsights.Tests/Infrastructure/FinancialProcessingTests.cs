using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BuildingBlocks.Infrastructure.Security;
using ClosedXML.Excel;
using Microsoft.Extensions.Configuration;
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
        var csv = await extractor.ExtractAsync(
            Encoding.UTF8.GetBytes("date,description,amount\n2026-01-01,Deposit,100"),
            "text/csv",
            CancellationToken.None
        );
        Assert.Single(csv);
        Assert.Null(csv[0].ImageBytes);
        Assert.Contains("Deposit", csv[0].Text);

        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Checking");
        sheet.Cell("A1").Value = "Merchant";
        sheet.Cell("B1").Value = "25.00";
        using var data = new MemoryStream();
        workbook.SaveAs(data);
        var xlsx = await extractor.ExtractAsync(
            data.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            CancellationToken.None
        );
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
            return JsonResponse(
                new
                {
                    status = "completed",
                    output = new[]
                    {
                        new
                        {
                            type = "message",
                            content = new[]
                            {
                                new { type = "output_text", text = "{\"movements\":[],\"needsReview\":true}" },
                            },
                        },
                    },
                    usage = new { input_tokens = 120, output_tokens = 40 },
                }
            );
        });
        var client = new OpenAiStatementClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.com/v1/") },
            Options.Create(new OpenAiFinancialOptions { ApiKey = "test-only", Model = "gpt-5.4-mini" })
        );

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

    // En producción, un estado de cuenta de 6 páginas y ~100 movimientos se cortaba en una sola respuesta.
    [Fact]
    public async Task Long_statement_is_analyzed_in_chunks_and_the_movements_are_merged()
    {
        var bodies = new List<string>();
        var handler = new StubHandler(async request =>
        {
            var body = await request.Content!.ReadAsStringAsync();
            bodies.Add(body);
            var page = int.Parse(Regex.Match(body, @"Page (\d+) extracted text").Groups[1].Value);
            var movement =
                "{\"transactionDate\":\"2025-01-02\",\"description\":\"Chunk "
                + page
                + "\",\"amount\":10,\"currency\":\"USD\",\"direction\":\"debit\",\"suggestedCategory\":\"Other\",\"sourcePage\":"
                + page
                + "}";
            return await Completed(
                "{\"movements\":[" + movement + "],\"needsReview\":" + (bodies.Count == 2 ? "true" : "false") + "}"
            );
        });
        var pages = Enumerable.Range(1, 10).Select(n => new ExtractedPage(n, new string('x', 3_500))).ToList();

        var result = await OpenAi(handler).AnalyzeAsync(pages, CancellationToken.None);

        var pagesPerRequest = bodies.Select(b => Regex.Matches(b, "extracted text").Count).ToList();
        Assert.True(bodies.Count > 1);
        Assert.All(pagesPerRequest, count => Assert.InRange(count, 1, 4));
        Assert.Equal(pages.Count, pagesPerRequest.Sum());
        Assert.Equal(bodies.Count, result.Movements.Count);
        Assert.Equal(120 * bodies.Count, result.InputTokens);
        Assert.True(result.NeedsReview);
    }

    [Fact]
    public async Task Request_keeps_reasoning_low_and_leaves_room_for_the_movements()
    {
        string? body = null;
        var handler = new StubHandler(async request =>
        {
            body = await request.Content!.ReadAsStringAsync();
            return await Completed("{\"movements\":[],\"needsReview\":false}");
        });

        await OpenAi(handler).AnalyzeAsync([new ExtractedPage(1, "statement text")], CancellationToken.None);

        using var json = JsonDocument.Parse(body!);
        Assert.Equal("low", json.RootElement.GetProperty("reasoning").GetProperty("effort").GetString());
        Assert.True(json.RootElement.GetProperty("max_output_tokens").GetInt32() >= 16_000);
    }

    [Fact]
    public async Task Cut_response_says_why_OpenAI_stopped()
    {
        var handler = new StubHandler(_ =>
            Task.FromResult(
                JsonResponse(
                    new
                    {
                        status = "incomplete",
                        incomplete_details = new { reason = "max_output_tokens" },
                        output = Array.Empty<object>(),
                    }
                )
            )
        );

        var error = await Assert.ThrowsAsync<DocumentProcessingException>(() =>
            OpenAi(handler).AnalyzeAsync([new ExtractedPage(1, "statement text")], CancellationToken.None)
        );

        Assert.Equal("OpenAiIncomplete", error.Code);
        Assert.Contains("max_output_tokens", error.Message);
    }

    [Fact]
    public void A_cut_response_goes_to_review_instead_of_failing()
    {
        // Reintentar no sirve: el mismo documento se corta igual. Queda para revisión, como un documento muy largo.
        Assert.True(FinancialOcrWorker.HoldsForReview("OpenAiIncomplete"));
        Assert.True(FinancialOcrWorker.HoldsForReview("DocumentLimit"));
        Assert.False(FinancialOcrWorker.HoldsForReview("OpenAiRequestFailed"));
    }

    // WellsFargo de producción: 9 páginas y 38 517 caracteres con pdftotext -layout.
    [Fact]
    public void Configured_limits_fit_a_real_ten_page_statement()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "TaxVision.slnx")))
            root = root.Parent;
        var settings = Path.Combine(
            root!.FullName,
            "src",
            "Services",
            "FinancialInsights",
            "TaxVision.FinancialInsights.Api",
            "appsettings.json"
        );
        var options = new ConfigurationBuilder()
            .AddJsonFile(settings)
            .Build()
            .GetSection(FinancialProcessingOptions.Section)
            .Get<FinancialProcessingOptions>()!;

        Assert.True(options.MaxPages >= 30);
        Assert.True(options.MaxTextCharacters >= 120_000);
        Assert.True(new FinancialProcessingOptions().MaxTextCharacters >= 120_000);
    }

    [Fact]
    public async Task Extractor_accepts_a_statement_of_forty_thousand_characters()
    {
        var extractor = new LocalStatementExtractor(Options.Create(new FinancialProcessingOptions()));
        var csv =
            "date,description,amount\n"
            + string.Concat(Enumerable.Repeat("2025-01-02,Zelle money sent,-50.00\n", 1_200));

        var pages = await extractor.ExtractAsync(Encoding.UTF8.GetBytes(csv), "text/csv", CancellationToken.None);

        Assert.True(pages[0].Text.Length > 40_000);
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
            return Task.FromResult(
                JsonResponse(
                    new
                    {
                        id = fileId,
                        ownerType = "Customer",
                        ownerId = customerId,
                        status = "PendingScan",
                        detectedContentType = "application/pdf",
                        sizeBytes = 100,
                    }
                )
            );
        });
        var source = new CloudStorageStatementSource(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5330/") },
            new StubTokens(),
            Options.Create(new FinancialProcessingOptions())
        );
        var error = await Assert.ThrowsAsync<DocumentProcessingException>(() =>
            source.DownloadAsync(Guid.NewGuid(), customerId, fileId, CancellationToken.None)
        );
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
            return Task.FromResult(
                requests == 1
                    ? JsonResponse(
                        new
                        {
                            id = fileId,
                            ownerType = "Customer",
                            ownerId = customerId,
                            status = "Available",
                            detectedContentType = "application/pdf",
                            sizeBytes = 100,
                        }
                    )
                    : JsonResponse(
                        new
                        {
                            fileId,
                            downloadUrl = "http://localhost:5124/internal/secret",
                            expiresAtUtc = DateTime.UtcNow.AddMinutes(5),
                        }
                    )
            );
        });
        var source = new CloudStorageStatementSource(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5330/") },
            new StubTokens(),
            Options.Create(new FinancialProcessingOptions())
        );
        var error = await Assert.ThrowsAsync<DocumentProcessingException>(() =>
            source.DownloadAsync(Guid.NewGuid(), customerId, fileId, CancellationToken.None)
        );
        Assert.Equal("StorageDownloadUrl", error.Code);
        Assert.Equal(2, requests);
    }

    private static OpenAiStatementClient OpenAi(HttpMessageHandler handler) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.com/v1/") },
            Options.Create(new OpenAiFinancialOptions { ApiKey = "test-only", Model = "gpt-5.4-mini" })
        );

    private static Task<HttpResponseMessage> Completed(string outputText) =>
        Task.FromResult(
            JsonResponse(
                new
                {
                    status = "completed",
                    output = new[]
                    {
                        new { type = "message", content = new[] { new { type = "output_text", text = outputText } } },
                    },
                    usage = new { input_tokens = 120, output_tokens = 40 },
                }
            )
        );

    private static HttpResponseMessage JsonResponse(object data) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(data), Encoding.UTF8, "application/json"),
        };

    private sealed class StubTokens : IServiceTokenAcquirer
    {
        public Task<string?> GetTokenAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<string?>("service-test-token");
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => send(request);
    }
}
