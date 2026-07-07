using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using AuthzEntitlements.Bank.Web.Clients;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace AuthzEntitlements.Bank.Web.Tests;

/// <summary>
/// CS60 — Docker-free integration test that boots the real Bank.Web app in-process
/// (<see cref="WebApplicationFactory{TEntryPoint}"/>) and exercises the static-SSR
/// <c>Approvals</c> page as an authenticated <c>teller1</c>.
///
/// <para>Regression guard for the antiforgery bug: a Blazor SSR <c>EditForm</c> with a
/// <c>FormName</c> <em>automatically</em> emits a hidden <c>__RequestVerificationToken</c>,
/// so adding an explicit <c>&lt;AntiforgeryToken /&gt;</c> inside the form produced a
/// <em>duplicate</em> hidden field. On POST the duplicated field yields a corrupt request
/// token and antiforgery validation fails with "A valid antiforgery token was not provided
/// with the request" (HTTP 400) — masking the intended fail-closed authorization outcome
/// (teller1 is not a checker → 403). This test asserts a single token per form and that a
/// teller's approve POST reaches the fail-closed decision rather than the antiforgery error.</para>
/// </summary>
public sealed class ApprovalsAntiforgeryTests
{
    private readonly ITestOutputHelper _output;

    public ApprovalsAntiforgeryTests(ITestOutputHelper output) => _output = output;

    // A single pending, at/above-threshold transaction so the Approvals page renders its
    // approve/reject EditForms (the forms are gated on `_pending.Count > 0`).
    private static readonly Guid PendingTransactionId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid Teller1BankUserId = Guid.Parse("40000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task Approvals_editforms_render_exactly_one_antiforgery_token_each()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        using var response = await client.GetAsync("/approvals");
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"GET /approvals should render 200 for an authenticated user but was {(int)response.StatusCode}.");

        // The page renders one EditForm to approve and one to reject when a pending item exists.
        var formCount = Regex.Matches(html, "<form\\b").Count;
        Assert.True(formCount >= 2, $"the approvals page should render the approve + reject forms but had {formCount} <form> element(s). HTML:\n{html}");

        // Blazor's EditForm auto-emits the antiforgery hidden field; there must be exactly one
        // per form. Two per form is the duplicate-token bug (explicit <AntiforgeryToken /> plus
        // the framework-injected one) that breaks the POST.
        var tokenCount = Regex.Matches(html, "name=\"__RequestVerificationToken\"").Count;
        Assert.True(
            tokenCount == formCount,
            $"each of the {formCount} EditForm(s) must carry exactly one __RequestVerificationToken " +
            $"(EditForm injects it automatically), but the page had {tokenCount}. More than one per form " +
            "is the duplicate-<AntiforgeryToken /> regression that fails the POST antiforgery check.");
    }

    [Fact]
    public async Task Teller_approve_post_is_not_blocked_by_antiforgery_and_fails_closed()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        // GET the page first: this renders the antiforgery token AND sets the antiforgery cookie
        // (the CreateClient handler stores it and replays it on the POST).
        using var getResponse = await client.GetAsync("/approvals");
        var html = await getResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var approveForm = ExtractApproveForm(html);
        _output.WriteLine("---- approve form ----");
        _output.WriteLine(approveForm);

        // Replay the form exactly as the browser would: every hidden field it renders (the
        // antiforgery token(s) + the Blazor _handler form-name field) plus the selected id.
        var fields = new List<KeyValuePair<string, string>>();
        foreach (var token in ExtractHiddenValues(approveForm, "__RequestVerificationToken"))
        {
            fields.Add(new("__RequestVerificationToken", token));
        }

        var handler = ExtractHiddenValues(approveForm, "_handler").FirstOrDefault() ?? "approve";
        fields.Add(new("_handler", handler));

        var selectName = ExtractSelectName(approveForm);
        Assert.False(string.IsNullOrEmpty(selectName), $"could not find the transaction <select> name in the approve form:\n{approveForm}");
        fields.Add(new(selectName!, PendingTransactionId.ToString()));

        using var postResponse = await client.PostAsync("/approvals", new FormUrlEncodedContent(fields));
        var postBody = await postResponse.Content.ReadAsStringAsync();

        // The bug: antiforgery rejects the POST (HTTP 400) before the component runs, so the
        // fail-closed server decision is never reached. Post-fix, the POST is accepted, the
        // component calls Bank.Api, and the teller's ineligible decision is surfaced as "denied".
        Assert.False(
            postBody.Contains("antiforgery", StringComparison.OrdinalIgnoreCase),
            $"the teller approve POST must NOT fail the antiforgery check (HTTP {(int)postResponse.StatusCode}). " +
            $"Body:\n{postBody}");
        Assert.Equal(HttpStatusCode.OK, postResponse.StatusCode);
        Assert.Contains("Decision denied", postBody);
    }

    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactoryFixture();

    // Bank.Web's Program requires a Keycloak authority and wires OIDC; the fixture supplies a
    // dummy authority (never contacted) and overrides authentication with a Test scheme that
    // signs the request in as teller1, plus a deterministic fake Bank.Api client.
    private sealed class WebApplicationFactoryFixture : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.UseSetting("Keycloak:Authority", "http://localhost:5/realms/authz-bank-test");
            builder.UseEnvironment("Development");

            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IBankApiClient>(new FakeBankApiClient());

                services.AddAuthentication(options =>
                {
                    options.DefaultScheme = "Test";
                    options.DefaultAuthenticateScheme = "Test";
                    options.DefaultChallengeScheme = "Test";
                }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });
            });
        }
    }

    // Authenticates every request as teller1 with the Keycloak-shaped claims Bank.Web reads
    // (preferred_username / tenant / roles). ClaimTypes.Name is set so the antiforgery token's
    // username binding has a stable value.
    private sealed class TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new[]
            {
                new Claim(ClaimTypes.Name, "teller1"),
                new Claim("preferred_username", "teller1"),
                new Claim("tenant", "CONTOSO"),
                new Claim("roles", "Teller"),
            };
            var identity = new ClaimsIdentity(claims, "Test");
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), "Test");
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }

    // Deterministic Bank.Api stand-in: one pending approval so the forms render, teller1 mapped
    // to a bank user so the checker id resolves, and a fail-closed 403 on approve/reject (a
    // Teller is not a checker) — exactly the server outcome the UI should surface.
    private sealed class FakeBankApiClient : IBankApiClient
    {
        private static readonly TransactionDto Pending = new(
            Id: PendingTransactionId,
            AccountId: Guid.Parse("50000000-0000-0000-0000-000000000001"),
            TenantId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            BranchId: Guid.Parse("20000000-0000-0000-0000-000000000001"),
            Type: TransactionType.Debit,
            Amount: 15_000m,
            Currency: "USD",
            Status: TransactionStatus.Pending,
            MakerId: Teller1BankUserId,
            CreatedAt: DateTimeOffset.UnixEpoch,
            Reference: "E2E-PENDING",
            Approval: new ApprovalDto(
                Id: Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                TransactionId: PendingTransactionId,
                MakerId: Teller1BankUserId,
                CheckerId: null,
                Status: ApprovalStatus.Pending,
                DecisionReason: null,
                RequestedAt: DateTimeOffset.UnixEpoch,
                DecidedAt: null));

        public Task<IReadOnlyList<TransactionDto>> GetTransactionsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TransactionDto>>([Pending]);

        public Task<IReadOnlyList<UserDto>> GetUsersAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<UserDto>>(
            [
                new UserDto(
                    Teller1BankUserId,
                    Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    Guid.Parse("20000000-0000-0000-0000-000000000001"),
                    "teller1",
                    "teller1@contoso.example",
                    "Tara Teller",
                    ["Teller"]),
            ]);

        private static ApiResult<TransactionDto> Denied() =>
            ApiResult<TransactionDto>.Failure(
                403,
                "Checker teller1 is not authorized to decide approvals (requires BranchManager or ComplianceOfficer).");

        public Task<ApiResult<TransactionDto>> ApproveTransactionAsync(
            Guid id, DecideRequest req, CancellationToken ct = default) =>
            Task.FromResult(Denied());

        public Task<ApiResult<TransactionDto>> RejectTransactionAsync(
            Guid id, DecideRequest req, CancellationToken ct = default) =>
            Task.FromResult(Denied());

        public Task<IReadOnlyList<AccountDto>> GetAccountsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AccountDto>>([]);

        public Task<AccountDto?> GetAccountAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult<AccountDto?>(null);

        public Task<TransactionDto?> GetTransactionAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult<TransactionDto?>(id == PendingTransactionId ? Pending : null);

        public Task<ApiResult<TransactionDto>> CreateTransactionAsync(
            CreateTransactionRequest req, CancellationToken ct = default) =>
            Task.FromResult(ApiResult<TransactionDto>.Failure(400, "not used"));
    }

    // ---- tiny HTML helpers (regex over the rendered SSR markup) ----

    // Returns the substring of the first <form>…</form> (the approve form is rendered first).
    private static string ExtractApproveForm(string html)
    {
        var start = html.IndexOf("<form", StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return html;
        }

        var end = html.IndexOf("</form>", start, StringComparison.OrdinalIgnoreCase);
        return end < 0 ? html[start..] : html[start..(end + "</form>".Length)];
    }

    // All values of the hidden input(s) with the given name (pre-fix there are two antiforgery ones).
    private static IEnumerable<string> ExtractHiddenValues(string formHtml, string name)
    {
        foreach (Match input in Regex.Matches(formHtml, "<input\\b[^>]*>", RegexOptions.IgnoreCase))
        {
            var tag = input.Value;
            if (!Regex.IsMatch(tag, $"\\bname=\"{Regex.Escape(name)}\""))
            {
                continue;
            }

            var value = Regex.Match(tag, "\\bvalue=\"([^\"]*)\"");
            if (value.Success)
            {
                yield return WebUtility.HtmlDecode(value.Groups[1].Value);
            }
        }
    }

    private static string? ExtractSelectName(string formHtml)
    {
        var match = Regex.Match(formHtml, "<select\\b[^>]*\\bname=\"([^\"]+)\"", RegexOptions.IgnoreCase);
        return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value) : null;
    }
}
