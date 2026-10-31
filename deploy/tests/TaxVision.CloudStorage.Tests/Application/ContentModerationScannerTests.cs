using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Moderation;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Moderation;
using TaxVision.CloudStorage.Tests.Domain;

namespace TaxVision.CloudStorage.Tests.Application;

/// <summary>La decisión de CloudStorage sobre la puntuación del scanner, incluida la falla cerrada.</summary>
public sealed class ContentModerationScannerTests
{
    [Fact]
    public async Task A_pdf_or_an_excluded_folder_is_not_sent_to_the_scanner()
    {
        var fixture = new Fixture();

        Assert.Equal(ContentScanVerdict.Clean, (await fixture.ScanAsync("application/pdf")).Verdict);
        Assert.Equal(ContentScanVerdict.Clean, (await fixture.ScanAsync("video/webm", FolderType.Recordings)).Verdict);
        Assert.Equal(0, fixture.Classifier.Calls);
    }

    [Theory]
    [InlineData(0.97, ContentScanVerdict.PolicyViolation)]
    [InlineData(0.70, ContentScanVerdict.Uncertain)]
    [InlineData(0.05, ContentScanVerdict.Clean)]
    public async Task The_score_is_judged_with_the_policy_thresholds(double score, ContentScanVerdict expected)
    {
        var fixture = new Fixture();
        fixture.Classifier.Score = score;

        var result = await fixture.ScanAsync("image/jpeg");

        Assert.Equal(expected, result.Verdict);
        Assert.Equal(new ContentSampling(2, 30), fixture.Classifier.LastSampling);
    }

    [Fact]
    public async Task Fail_closed_puts_the_file_in_review_when_the_scanner_is_down_or_missing()
    {
        var down = new Fixture();
        down.Classifier.Throw = new ContentClassifierUnavailableException("down");
        Assert.Equal(ContentScanVerdict.Uncertain, (await down.ScanAsync("image/png")).Verdict);

        var missing = new Fixture();
        missing.Classifier.Configured = false;
        Assert.Equal(ContentScanVerdict.Uncertain, (await missing.ScanAsync("image/png")).Verdict);
    }

    [Fact]
    public async Task Fail_open_publishes_when_the_scanner_is_down()
    {
        var fixture = new Fixture(ContentModerationPolicyTests.Strict(failClosed: false));
        fixture.Classifier.Throw = new ContentClassifierUnavailableException("down");

        Assert.Equal(ContentScanVerdict.Clean, (await fixture.ScanAsync("image/png")).Verdict);
    }

    [Fact]
    public async Task Undecodable_media_and_oversized_files_go_to_review()
    {
        var undecodable = new Fixture();
        undecodable.Classifier.Throw = new ContentNotDecodableException("bad");
        Assert.Equal(ContentScanVerdict.Uncertain, (await undecodable.ScanAsync("image/png")).Verdict);

        var oversized = new Fixture(ContentModerationPolicyTests.Strict(maxScanBytes: 2));
        Assert.Equal(ContentScanVerdict.Uncertain, (await oversized.ScanAsync("video/mp4")).Verdict);
        Assert.Equal(0, oversized.Classifier.Calls);
    }

    [Fact]
    public async Task Without_the_policy_row_nothing_is_moderated_and_the_lookup_is_cached()
    {
        var fixture = Fixture.WithoutPolicy();

        Assert.Equal(ContentScanVerdict.Clean, (await fixture.ScanAsync("image/png")).Verdict);
        await fixture.ScanAsync("image/png");

        Assert.Equal(1, fixture.Policies.Reads);
        Assert.Equal(0, fixture.Classifier.Calls);
    }

    private sealed class Fixture
    {
        private readonly ContentModerationScanner _scanner;

        public Fixture(ContentModerationSettings? settings = null)
            : this(ContentModerationPolicy.Create(settings ?? ContentModerationPolicyTests.Strict(), Guid.NewGuid(), DateTime.UtcNow).Value) { }

        private Fixture(ContentModerationPolicy? policy)
        {
            Policies = new FakePolicyRepository(policy);
            _scanner = new ContentModerationScanner(
                Policies,
                new ContentModerationPolicyCache(),
                Classifier,
                new FakeSystemClock(DateTime.UtcNow),
                NullLogger<ContentModerationScanner>.Instance
            );
        }

        public static Fixture WithoutPolicy() => new((ContentModerationPolicy?)null);

        public FakePolicyRepository Policies { get; }
        public FakeClassifier Classifier { get; } = new();

        public Task<ContentScanResult> ScanAsync(string contentType, FolderType folder = FolderType.Other) =>
            _scanner.ScanAsync(
                new MemoryStream([1, 2, 3, 4]),
                new ContentScanContext(Guid.NewGuid(), Guid.NewGuid(), OwnerType.Tenant, "x", contentType, folder),
                CancellationToken.None
            );
    }

    private sealed class FakePolicyRepository(ContentModerationPolicy? policy) : IContentModerationPolicyRepository
    {
        public int Reads { get; private set; }

        public Task<ContentModerationPolicy?> GetAsync(CancellationToken ct)
        {
            Reads++;
            return Task.FromResult(policy);
        }
    }

    private sealed class FakeClassifier : IContentClassifier
    {
        public bool Configured { get; set; } = true;
        public double Score { get; set; }
        public Exception? Throw { get; set; }
        public int Calls { get; private set; }
        public ContentSampling? LastSampling { get; private set; }

        public bool IsConfigured => Configured;

        public Task<ContentClassification> ClassifyAsync(
            Stream content,
            string contentType,
            ContentSampling sampling,
            CancellationToken ct
        )
        {
            Calls++;
            LastSampling = sampling;
            if (Throw is not null)
                throw Throw;
            return Task.FromResult(new ContentClassification(Score, 1));
        }
    }
}
