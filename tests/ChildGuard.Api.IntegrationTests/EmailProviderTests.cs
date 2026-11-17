using System.Net;
using System.Text;
using ChildGuard.Application.Common;
using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Entities;
using ChildGuard.Infrastructure.Identity;
using ChildGuard.Infrastructure.Notifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ChildGuard.Api.IntegrationTests;

/// <summary>
/// Pure unit tests (no database, no network) for the email provider layer: the Resend testing-sender policy,
/// the Resend and SMTP senders, and the OTP sender's failure and logging behaviour.
/// </summary>
public class EmailProviderTests
{
    private const string Testing = "ChildGuard <onboarding@resend.dev>";
    private const string Owner = "owner@example.test";
    private static readonly EmailMessage Msg =
        new("guardian@example.test", "Guardian", "Your code", "<p>123456</p>", "123456");

    // ---- ResendSandbox ----

    [Fact]
    public void Sandbox_TestingSender_RedirectsToTestRecipient_AndMarksTheIntendedRecipient()
    {
        var r = ResendSandbox.Apply(Msg, Testing, Owner);

        Assert.True(r.Succeeded);
        Assert.Equal(Owner, r.Value!.ToEmail);
        Assert.Contains("guardian@example.test", r.Value.Subject);
        Assert.Contains("TEST MODE", r.Value.HtmlBody);
        Assert.Contains("<p>123456</p>", r.Value.HtmlBody);
        Assert.Contains("guardian@example.test", r.Value.TextBody);
        Assert.Contains("123456", r.Value.TextBody);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Sandbox_TestingSender_WithoutTestRecipient_Refuses(string? recipient)
    {
        var r = ResendSandbox.Apply(Msg, Testing, recipient);

        Assert.False(r.Succeeded);
        Assert.Contains("Email:Resend:TestRecipient", r.Error);
    }

    [Fact]
    public void Sandbox_MessageAlreadyForTestRecipient_IsUntouched()
    {
        var own = Msg with { ToEmail = "OWNER@example.test" };
        var r = ResendSandbox.Apply(own, Testing, Owner);

        Assert.True(r.Succeeded);
        Assert.Equal(own, r.Value);
    }

    [Fact]
    public void Sandbox_VerifiedDomainSender_PassesThrough()
    {
        var r = ResendSandbox.Apply(Msg, "School <noreply@school.example>", null);

        Assert.True(r.Succeeded);
        Assert.Equal(Msg, r.Value);
    }

    [Theory]
    [InlineData("onboarding@resend.dev", true)]
    [InlineData("ChildGuard <Onboarding@RESEND.DEV>", true)]
    [InlineData("noreply@school.example", false)]
    [InlineData("noreply@notresend.dev", false)]
    [InlineData("not an address", false)]
    public void Sandbox_RecognisesTheTestingSender(string from, bool expected) =>
        Assert.Equal(expected, ResendSandbox.IsTestingSender(from));

    [Fact]
    public void Sandbox_IntendedAddressInBanner_IsHtmlEncoded()
    {
        var evil = Msg with { ToEmail = "a<script>@example.test" };
        var r = ResendSandbox.Apply(evil, Testing, Owner);

        Assert.DoesNotContain("<script>", r.Value!.HtmlBody.Split("<p>123456</p>")[0]);
    }

    // ---- ResendEmailSender ----

    private sealed class StubHandler(HttpStatusCode status, string body = "{}") : HttpMessageHandler
    {
        public List<string> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct));
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private static ResendEmailSender Resend(StubHandler handler, string from = Testing, string? recipient = Owner) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://api.resend.test") },
            new EmailOptions { From = from, Resend = new ResendEmailOptions { TestRecipient = recipient } },
            NullLogger<ResendEmailSender>.Instance);

    [Fact]
    public async Task Resend_TestingSenderWithoutRecipient_FailsWithoutAnyHttpCall()
    {
        var handler = new StubHandler(HttpStatusCode.OK);
        var result = await Resend(handler, recipient: null).SendAsync(Msg);

        Assert.False(result.Succeeded);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Resend_TestingSender_SendsOnlyToTestRecipient()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{\"id\":\"x\"}");
        var result = await Resend(handler).SendAsync(Msg);

        Assert.True(result.Succeeded);
        var payload = Assert.Single(handler.Requests);
        Assert.Contains(Owner, payload);
        Assert.DoesNotContain("\"guardian@example.test\"", payload.Split("\"subject\"")[0]);
    }

    [Fact]
    public async Task Resend_VerifiedDomain_SendsToTheRealRecipient()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{\"id\":\"x\"}");
        var result = await Resend(handler, "School <noreply@school.example>", null).SendAsync(Msg);

        Assert.True(result.Succeeded);
        Assert.Contains("guardian@example.test", Assert.Single(handler.Requests));
    }

    [Fact]
    public async Task Resend_ProviderRejection_BecomesFailure()
    {
        var handler = new StubHandler(HttpStatusCode.Forbidden, "{\"message\":\"domain not verified\",\"name\":\"validation_error\"}");
        var result = await Resend(handler).SendAsync(Msg);

        Assert.False(result.Succeeded);
        Assert.Contains("domain not verified", result.Error);
    }

    // ---- SmtpEmailSender ----

    [Fact]
    public void Smtp_Build_HasTextBodyAndHtmlAlternative()
    {
        using var mail = SmtpEmailSender.Build(Msg, "ChildGuard <sender@example.test>");

        Assert.Equal("sender@example.test", mail.From!.Address);
        Assert.Equal("guardian@example.test", Assert.Single(mail.To).Address);
        Assert.Equal("123456", mail.Body);
        Assert.Single(mail.AlternateViews);
    }

    [Fact]
    public async Task Smtp_UnreachableHost_ReturnsFailure_WithoutLeakingCredentials()
    {
        var logs = new CapturingLogger<SmtpEmailSender>();
        var sender = new SmtpEmailSender(new EmailOptions
        {
            From = "sender@example.test",
            Smtp = new SmtpEmailOptions { Host = "127.0.0.1", Port = 1, UseSsl = false, Username = "u", Password = "super-secret-pw" }
        }, logs);

        var result = await sender.SendAsync(Msg);

        Assert.False(result.Succeeded);
        Assert.DoesNotContain("super-secret-pw", result.Error);
        Assert.DoesNotContain(logs.Lines, l => l.Contains("super-secret-pw") || l.Contains("123456"));
    }

    // ---- EmailOtpSender ----

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Lines { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add(formatter(state, exception));
    }

    private sealed class StubEmail(string? failWith) : IEmailSender
    {
        public Task<Result> SendAsync(EmailMessage message, CancellationToken ct = default) =>
            Task.FromResult(failWith is null ? Result.Success() : Result.Failure(failWith));
    }

    private static readonly Guardian Guardian = new() { GuardianId = 7, FullName = "G", Email = "guardian@example.test" };

    [Fact]
    public async Task OtpSender_Success_ReturnsSuccess_AndLogsNoCode()
    {
        var logs = new CapturingLogger<EmailOtpSender>();
        var result = await new EmailOtpSender(new StubEmail(null), new OtpOptions { LogCodeOnDeliveryFailure = true }, logs, true)
            .SendLoginOtpAsync(Guardian, "654321", TimeSpan.FromMinutes(5));

        Assert.True(result.Succeeded);
        Assert.DoesNotContain(logs.Lines, l => l.Contains("654321"));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]   // option on, but not Development
    [InlineData(false, true)]   // Development, but option off
    public async Task OtpSender_Failure_IsReturned_AndCodeNotLogged(bool allowDev, bool optionOn)
    {
        var logs = new CapturingLogger<EmailOtpSender>();
        var result = await new EmailOtpSender(new StubEmail("boom"), new OtpOptions { LogCodeOnDeliveryFailure = optionOn }, logs, allowDev)
            .SendLoginOtpAsync(Guardian, "654321", TimeSpan.FromMinutes(5));

        Assert.False(result.Succeeded);
        Assert.Equal("boom", result.Error);
        Assert.DoesNotContain(logs.Lines, l => l.Contains("654321"));
    }

    [Fact]
    public async Task OtpSender_Failure_LogsCodeOnlyWhenDevelopmentAndOptionOn()
    {
        var logs = new CapturingLogger<EmailOtpSender>();
        await new EmailOtpSender(new StubEmail("boom"), new OtpOptions { LogCodeOnDeliveryFailure = true }, logs, true)
            .SendLoginOtpAsync(Guardian, "654321", TimeSpan.FromMinutes(5));

        Assert.Contains(logs.Lines, l => l.Contains("654321"));
    }
}
