using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Postmaster.Application.Abstractions;
using TaxVision.Postmaster.Application.Sending;
using TaxVision.Postmaster.Domain.Sending;
using TaxVision.Postmaster.Infrastructure.DevMailbox;

namespace TaxVision.Postmaster.Tests.DevMailbox;

public sealed class DevMailboxWriterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pm-devmailbox-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (System.IO.Directory.Exists(_root))
            System.IO.Directory.Delete(_root, recursive: true);
    }

    private sealed class FakeHostEnvironment(string environmentName, string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "TaxVision.Postmaster.Api";
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakeEmailSender : IEmailSender
    {
        public int Calls { get; private set; }

        public Task<SendResult> SendAsync(
            SentMessage message,
            RenderedContent content,
            ResolvedEmailProvider provider,
            IReadOnlyList<InlineAssetBytes> inlineAssets,
            IReadOnlyList<OutboundAttachmentBytes> attachments,
            CancellationToken ct
        )
        {
            Calls++;
            return Task.FromResult(new SendResult(true, "<id@x>", null, []));
        }
    }

    private sealed class FakeConnectedMailboxSender : IConnectedMailboxSender
    {
        public int Calls { get; private set; }

        public Task<SendResult> SendAsync(
            SentMessage message,
            RenderedContent content,
            ResolvedMailbox provider,
            string? inReplyToInternetMessageId,
            IReadOnlyList<string>? references,
            string? replyToProviderMessageId,
            IReadOnlyList<OutboundAttachmentBytes> attachments,
            IReadOnlyList<InlineAssetBytes> inlineAssets,
            CancellationToken ct
        )
        {
            Calls++;
            return Task.FromResult(new SendResult(true, "draft-1", null, []));
        }
    }

    private static readonly DateTimeOffset Now = new(2026, 10, 28, 14, 5, 9, 123, TimeSpan.Zero);

    private DevMailboxWriter CreateWriter(
        string environment,
        bool enabled = true,
        string? directory = null,
        TimeProvider? time = null
    ) =>
        new(
            new DevMailboxOptions { Enabled = enabled, Directory = directory ?? "mailbox" },
            new FakeHostEnvironment(environment, _root),
            NullLogger<DevMailboxWriter>.Instance,
            time ?? new FixedTimeProvider(Now)
        );

    private static SentMessage CreateMessage(string subject = "Your access code")
    {
        var message = SentMessage
            .Queue(
                Guid.NewGuid(),
                Guid.NewGuid().ToString("N"),
                subject,
                "noreply@taxoffix.example",
                EmailStream.Transactional,
                "smtp2go",
                Guid.NewGuid(),
                "corr-1",
                "TaxOffix",
                replyTo: null,
                "tasks.form-access-code",
                DateTime.UtcNow
            )
            .Value;
        message.AddRecipient("client@example.com", RecipientType.To, null);
        message.AddRecipient("audit@example.com", RecipientType.Cc, null);
        return message;
    }

    private static RenderedContent Content(string subject = "Your access code") =>
        new(
            subject,
            "<p>Open <a href=\"https://app.local/f/abc?t=1&amp;x=2\">form</a>. Code: 482913</p>",
            "Code: 482913"
        );

    private static ResolvedEmailProvider SmtpProvider() =>
        new("smtp2go", "mail.smtp2go.com", 587, true, "u", "p", "noreply@taxoffix.example", "TaxOffix", 60);

    private string MailboxDir => Path.Combine(_root, "mailbox");

    [Fact]
    public async Task Development_and_enabled_writes_a_copy_and_still_sends()
    {
        var writer = CreateWriter(Environments.Development);
        var inner = new FakeEmailSender();
        var sender = new DevMailboxEmailSender(inner, writer);

        var result = await sender.SendAsync(CreateMessage(), Content(), SmtpProvider(), [], [], CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, inner.Calls);
        var file = Assert.Single(System.IO.Directory.GetFiles(MailboxDir, "*.html"));
        Assert.Equal("20261028-140509-123_client@example.com_Your-access-code.html", Path.GetFileName(file));
        var html = await File.ReadAllTextAsync(file);
        Assert.Contains("To: client@example.com", html);
        Assert.Contains("Cc: audit@example.com", html);
        Assert.Contains("From: TaxOffix &lt;noreply@taxoffix.example&gt;", html);
        Assert.Contains("Subject: Your access code", html);
        Assert.Contains("Template: tasks.form-access-code", html);
        Assert.Contains("https://app.local/f/abc?t=1&amp;x=2", html);
        Assert.Contains("<pre id=\"dev-mailbox-text\"", html);
        Assert.Contains("Code: 482913</pre>", html);
    }

    [Fact]
    public async Task Connected_mailbox_path_also_writes_a_copy_and_still_sends()
    {
        var writer = CreateWriter(Environments.Development);
        var inner = new FakeConnectedMailboxSender();
        var sender = new DevMailboxConnectedMailboxSender(inner, writer);

        var result = await sender.SendAsync(
            CreateMessage(),
            Content(),
            new ResolvedMailbox(Guid.NewGuid(), "gmail", "sales@tenant.example", "Tenant Sales"),
            null,
            null,
            null,
            [],
            [],
            CancellationToken.None
        );

        Assert.True(result.Success);
        Assert.Equal(1, inner.Calls);
        var html = await File.ReadAllTextAsync(Assert.Single(System.IO.Directory.GetFiles(MailboxDir, "*.html")));
        Assert.Contains("From: Tenant Sales &lt;sales@tenant.example&gt;", html);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task Enabled_outside_development_is_ignored(string environment)
    {
        var writer = CreateWriter(environment);
        var inner = new FakeEmailSender();

        await new DevMailboxEmailSender(inner, writer).SendAsync(
            CreateMessage(),
            Content(),
            SmtpProvider(),
            [],
            [],
            CancellationToken.None
        );

        Assert.False(writer.IsActive);
        Assert.Null(writer.Directory);
        Assert.Equal(1, inner.Calls);
        Assert.False(System.IO.Directory.Exists(MailboxDir));
    }

    [Fact]
    public void Disabled_in_development_is_inactive()
    {
        var writer = CreateWriter(Environments.Development, enabled: false);

        writer.Write(CreateMessage(), Content(), "noreply@taxoffix.example", null);

        Assert.False(writer.IsActive);
        Assert.False(System.IO.Directory.Exists(MailboxDir));
    }

    [Fact]
    public void Default_directory_resolves_to_repo_logs_folder_from_the_api_content_root()
    {
        var apiRoot = Path.Combine(_root, "src", "Services", "Postmaster", "TaxVision.Postmaster.Api");
        var writer = new DevMailboxWriter(
            new DevMailboxOptions { Enabled = true },
            new FakeHostEnvironment(Environments.Development, apiRoot),
            NullLogger<DevMailboxWriter>.Instance,
            TimeProvider.System
        );

        Assert.Equal(Path.GetFullPath(Path.Combine(_root, "logs", "dev-mailbox")), writer.Directory);
    }

    [Fact]
    public async Task Writer_failure_does_not_break_the_send()
    {
        // La "carpeta" es un archivo: CreateDirectory falla y el writer debe tragarse el error.
        System.IO.Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(Path.Combine(_root, "blocked"), "not a directory");
        var writer = CreateWriter(Environments.Development, directory: "blocked");
        var inner = new FakeEmailSender();

        var result = await new DevMailboxEmailSender(inner, writer).SendAsync(
            CreateMessage(),
            Content(),
            SmtpProvider(),
            [],
            [],
            CancellationToken.None
        );

        Assert.True(writer.IsActive);
        Assert.True(result.Success);
        Assert.Equal(1, inner.Calls);
    }

    [Theory]
    [InlineData("Your code: 123/456 <b>!", "Your-code-123-456-b")]
    [InlineData("  ¿Firma pendiente?  ", "Firma-pendiente")]
    [InlineData("a\\b|c*d\"e?f", "a-b-c-d-e-f")]
    [InlineData("...", "no-subject")]
    [InlineData("", "no-subject")]
    public void Sanitize_removes_characters_invalid_in_file_names(string input, string expected)
    {
        Assert.Equal(expected, DevMailboxWriter.Sanitize(input, "no-subject"));
    }

    [Fact]
    public void Sanitize_truncates_long_values_and_file_name_has_expected_shape()
    {
        var name = DevMailboxWriter.BuildFileName(
            new DateTime(2026, 1, 2, 3, 4, 5, 6, DateTimeKind.Utc),
            "Client <x@y.com>",
            new string('a', 300)
        );

        Assert.Equal($"20260102-030405-006_Client-x@y.com_{new string('a', 60)}.html", name);
        Assert.Equal(-1, name.IndexOfAny(Path.GetInvalidFileNameChars()));
    }

    [Fact]
    public void Retention_keeps_only_the_newest_200_files()
    {
        var time = new FixedTimeProvider(Now);
        var writer = CreateWriter(Environments.Development, time: time);
        var message = CreateMessage();

        for (var i = 0; i < DevMailboxWriter.MaxFiles + 5; i++)
        {
            time.Now = Now.AddMilliseconds(i);
            writer.Write(message, Content(), "noreply@taxoffix.example", null);
        }

        var names = System.IO.Directory.GetFiles(MailboxDir, "*.html").Select(Path.GetFileName).Order().ToList();
        Assert.Equal(DevMailboxWriter.MaxFiles, names.Count);
        // Se borraron los 5 más viejos: el primero que queda es el sexto.
        Assert.StartsWith($"{Now.AddMilliseconds(5):yyyyMMdd-HHmmss-fff}_", names[0]);
        Assert.StartsWith($"{Now.AddMilliseconds(DevMailboxWriter.MaxFiles + 4):yyyyMMdd-HHmmss-fff}_", names[^1]);
    }

    [Fact]
    public void Same_millisecond_does_not_overwrite_the_previous_copy()
    {
        var writer = CreateWriter(Environments.Development);

        writer.Write(CreateMessage(), Content(), "noreply@taxoffix.example", null);
        writer.Write(CreateMessage(), Content(), "noreply@taxoffix.example", null);

        Assert.Equal(2, System.IO.Directory.GetFiles(MailboxDir, "*.html").Length);
    }
}
