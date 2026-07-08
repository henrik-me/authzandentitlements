namespace AuthzEntitlements.Bank.Web.ViewModels;

// The concern a flow step illustrates, so the diagram can colour-code authentication vs
// authorization vs commercial entitlement vs plain data movement.
public enum FlowAspect
{
    Data,
    AuthN,
    AuthZ,
    License,
}

// One annotation shown on a node (e.g. "validates the JWT audience" is an AuthN note).
public sealed record FlowNote(FlowAspect Aspect, string Text);

// A participant in the request path plus the call INTO it. Call is null for the origin node.
public sealed record FlowNode(string Name, string Role, string? Call, IReadOnlyList<FlowNote> Notes);

// A page's data-flow overview: a caption and the ordered participants the request travels through.
public sealed record DataFlow(string Caption, IReadOnlyList<FlowNode> Nodes);

// Per-page data-flow overviews rendered by the <AuthFlowDiagram> component. Kept as pure data
// (no server, no DI) so the AuthN/AuthZ story of every page is unit-testable offline and lives in
// one place. These describe the SHIPPED behaviour: token acquisition + forwarding + WWW-Authenticate
// handling (AuthN), what each hop validates (AuthZ), the commercial entitlement layer, and data.
public static class DataFlowDiagrams
{
    private static FlowNote AuthN(string text) => new(FlowAspect.AuthN, text);

    private static FlowNote AuthZ(string text) => new(FlowAspect.AuthZ, text);

    private static FlowNote License(string text) => new(FlowAspect.License, text);

    private static FlowNote Data(string text) => new(FlowAspect.Data, text);

    // Origin node shared by every diagram: the signed-in browser holding the OIDC cookie.
    private static FlowNode SignedInBrowser { get; } = new(
        "Browser",
        "signed-in user",
        null,
        [AuthN("Signed in via Keycloak OIDC; the access token is stored in the auth cookie (SaveTokens).")]);

    // Bank.Web hop for a static-SSR page that forwards the user's bearer to a bank API.
    private static FlowNode BankWebForwarding { get; } = new(
        "Bank.Web",
        "backend-for-frontend (static SSR)",
        "renders during the HTTP request",
        [
            AuthN("AccessTokenHandler reads the access token from HttpContext and attaches it as a Bearer."),
            AuthN("On a 401 it captures WWW-Authenticate (invalid_token/expired) and shows a 'session expired' notice."),
        ]);

    public static DataFlow Home { get; } = new(
        "Data flow — authentication (OIDC sign-in)",
        [
            new("Browser", "user's device", null,
                [AuthN("Starts sign-in at /login; no valid cookie yet.")]),
            new("Keycloak", "OpenID Connect provider (IdP)", "authorization-code flow + PKCE",
                [AuthN("Authenticates the user and issues an id_token + access token carrying roles, tenant, branch, scopes, and audience = bank-api.")]),
            new("Bank.Web", "backend-for-frontend", "redirect back to /signin-oidc with the code",
                [
                    AuthN("Exchanges the code for tokens server-to-server, validates the id_token nonce, and establishes the auth cookie (SaveTokens keeps the access token for later API calls)."),
                    AuthZ("[Authorize] pages then require this authenticated cookie; sign-out clears it (RP-initiated logout with id_token_hint)."),
                ]),
        ]);

    public static DataFlow Claims { get; } = new(
        "Data flow — reading your token (no backend call)",
        [
            SignedInBrowser,
            new("Bank.Web", "backend-for-frontend (static SSR)", "GET /claims (auth cookie)",
                [
                    AuthZ("[Authorize] — only a signed-in user may view the page."),
                    AuthN("Reads the ClaimsPrincipal straight from the OIDC auth cookie — there is NO backend call."),
                    AuthN("Displays the token's claims: sub, preferred_username, tenant, branch, roles, scope — the raw material every downstream AuthZ decision uses."),
                ]),
        ]);

    public static DataFlow Accounts { get; } = new(
        "Data flow — read path (AuthN → coarse gateway → fine API)",
        [
            SignedInBrowser,
            BankWebForwarding,
            new("Edge Gateway", "coarse authorization (YARP)", "GET /api/accounts + Bearer",
                [
                    AuthN("Validates the JWT: signature, issuer, audience = bank-api, and expiry (30s clock skew)."),
                    AuthZ("Coarse check: requires scope bank.read and a tenant claim — otherwise 401/403."),
                ]),
            new("Bank.Api", "fine authorization (domain API)", "forwarded GET /api/accounts + Bearer",
                [
                    AuthN("Independently re-validates the same JWT (defense in depth)."),
                    AuthZ("Fine check: tenant-scoped read — returns only the caller tenant's accounts."),
                    Data("200 accounts · 401 expired · 403 denied → back to Bank.Web → rendered."),
                ]),
        ]);

    // The single-account detail page rides the exact same read path as the account list.
    public static DataFlow AccountDetail { get; } = Accounts with
    {
        Caption = "Data flow — single-account read (AuthN → coarse gateway → fine API)",
    };

    public static DataFlow NewTransaction { get; } = new(
        "Data flow — maker create (coarse → fine → commercial entitlement)",
        [
            SignedInBrowser,
            BankWebForwarding,
            new("Edge Gateway", "coarse authorization (YARP)", "POST /api/transactions + Bearer",
                [
                    AuthN("Validates the JWT (signature/issuer/audience/expiry)."),
                    AuthZ("Coarse check: requires scope bank.transactions.write and a tenant claim."),
                ]),
            new("Bank.Api", "fine authorization (domain API)", "forwarded POST /api/transactions",
                [
                    AuthN("Re-validates the JWT."),
                    AuthZ("Fine check: the maker id must equal the token subject; the account's tenant must match the caller; amount > 0."),
                ]),
            new("Licensing service", "commercial license", "internal check: high-value gate (tenant plan)",
                [
                    License("Bank.Api asks the licensing service whether the tenant's plan licenses the transaction (e.g. high-value transfers) — anonymous intra-cluster call, distinct from AuthZ. Fail-closed to gated."),
                    Data("At/above threshold the transaction is routed to maker-checker approval instead of posting."),
                ]),
        ]);

    public static DataFlow Approvals { get; } = new(
        "Data flow — maker-checker decide (coarse → fine + segregation of duties)",
        [
            SignedInBrowser,
            BankWebForwarding,
            new("Edge Gateway", "coarse authorization (YARP)", "POST /api/transactions/{id}/approve + Bearer",
                [
                    AuthN("Validates the JWT (signature/issuer/audience/expiry)."),
                    AuthZ("Coarse check: requires scope bank.approvals.write and a tenant claim."),
                ]),
            new("Bank.Api", "fine authorization (domain API)", "forwarded approve / reject",
                [
                    AuthN("Re-validates the JWT; the checker id must equal the token subject."),
                    AuthZ("Fine check: checker role ∈ {BranchManager, ComplianceOfficer}; segregation of duties (checker ≠ maker); same tenant; decide-once."),
                    Data("403 (role), 409 (SoD / already decided), or 200 Posted/Rejected."),
                ]),
        ]);

    public static DataFlow AccessRequests { get; } = new(
        "Data flow — JIT access request (token-bound governance + PDP segregation of duties)",
        [
            SignedInBrowser,
            new("Bank.Web", "backend-for-frontend (static SSR)", "forwards the Bearer to governance",
                [
                    AuthN("Attaches the user's access token; on a 401 captures WWW-Authenticate and shows a 'session expired' notice."),
                ]),
            new("Governance.Service", "JIT access governance", "POST /api/governance/requests + Bearer",
                [
                    AuthN("Validates the JWT (audience bank-api)."),
                    AuthZ("Token-bound + tenant-scoped: the request/decision is bound to the signed-in subject, and requests are scoped to the caller's tenant."),
                ]),
            new("Authz PDP", "policy decision point", "internal segregation-of-duties check",
                [
                    AuthZ("Governance asks the PDP to evaluate segregation of duties — an approver may not approve their own request; a null decision fails closed (stays Pending)."),
                ]),
        ]);

    public static DataFlow Licensing { get; } = new(
        "Data flow — commercial licensing (not authorization)",
        [
            SignedInBrowser,
            new("Bank.Web", "backend-for-frontend (static SSR)", "GET /licensing (auth cookie)",
                [
                    AuthN("Reads the tenant claim from the cookie — the licensing service is anonymous, so NO bearer is forwarded."),
                ]),
            new("Licensing service", "commercial license", "GET plan summary + feature gates (tenant)",
                [
                    License("The commercial layer: what the tenant's subscription plan licenses — plan tier, feature gates, seat counts. Distinct from the gateway/PDP authorization; the service is the source of truth and the UI fails closed to gated."),
                ]),
        ]);

    public static DataFlow Playground { get; } = new(
        "Data flow — AuthZ what-if (PDP fan-out, enforces nothing)",
        [
            SignedInBrowser,
            new("Bank.Web", "backend-for-frontend (Interactive Server)", "live fan-out over the circuit",
                [
                    AuthN("Interactive Server has no per-event HttpContext, so NO token is forwarded — the PDP is anonymous and the page crafts the subject/roles/scopes itself."),
                ]),
            new("Authz PDP", "policy decision point (all engines)", "POST /playground/fanout (one crafted AccessRequest)",
                [
                    AuthZ("The PDP is the authorization decision: it evaluates the request across every engine (reference / OPA / Cedar / Casbin / …) and returns each engine's Permit/Deny + explanation."),
                    Data("What-if only — it enforces nothing and writes NO audit entry."),
                ]),
            new("Audit.Service", "tamper-evident log (read)", "optional GET /api/audit/entries (replay pre-fill)",
                [
                    Data("Anonymous read used only to pre-fill the form from a recorded decision's request snapshot."),
                ]),
        ]);

    public static DataFlow Audit { get; } = new(
        "Data flow — tamper-evident decision log (read model)",
        [
            SignedInBrowser,
            new("Bank.Web", "backend-for-frontend (static SSR)", "GET /audit (auth cookie)",
                [
                    AuthZ("[Authorize] gates the page, but the Audit service itself is anonymous (intra-cluster) — no bearer is forwarded."),
                ]),
            new("Audit.Service", "hash-chained read model", "GET /api/audit/entries + chain verification",
                [
                    Data("A tamper-evident, hash-chained log of decisions emitted by the gateway / Bank.Api / PDP; the page recomputes and verifies the chain."),
                ]),
        ]);

    public static DataFlow BreakGlass { get; } = new(
        "Data flow — break-glass emergency elevation (PDP + governance)",
        [
            SignedInBrowser,
            new("Bank.Web", "showcase (Interactive Server)", "live evaluation over the circuit",
                [
                    AuthN("PDP and the governance break-glass endpoints are anonymous in this lab — no token is forwarded."),
                ]),
            new("Authz PDP", "policy decision point", "evaluate base action, then re-evaluate WITH the grant",
                [
                    AuthZ("The base action is DENIED for a missing capability (MissingScope / RoleNotAuthorized); with an active, matching break-glass grant in context the PDP raises it to Permit + a require_break_glass_review obligation."),
                    AuthZ("It NEVER overrides an integrity invariant — tenant isolation and maker-checker / segregation of duties still deny."),
                ]),
            new("Governance.Service", "grant lifecycle", "issue / review the break-glass grant",
                [
                    AuthZ("Stores the bounded, auto-expiring grant and drives the mandatory (heightened-audited) post-review."),
                ]),
        ]);

    public static DataFlow Delegation { get; } = new(
        "Data flow — manager→delegate on-behalf-of (PDP + governance)",
        [
            SignedInBrowser,
            new("Bank.Web", "showcase (Interactive Server)", "live evaluation over the circuit",
                [
                    AuthN("PDP and governance are anonymous in this lab — no token is forwarded."),
                ]),
            new("Authz PDP", "policy decision point", "evaluate the action as the human vs via the delegate",
                [
                    AuthZ("On-behalf-of, not impersonation: the delegate is constrained to the INTERSECTION of the manager's rights and the delegated agent.bank.* scopes, and the PDP requires an active, matching delegation grant."),
                ]),
            new("Governance.Service", "grant lifecycle", "create / revoke the delegation grant",
                [
                    AuthZ("Stores the bounded, revocable manager→delegate grant that bounds what the delegate may exercise."),
                ]),
        ]);

    public static DataFlow AgentAccess { get; } = new(
        "Data flow — agent on-behalf-of (human vs agent, side by side)",
        [
            SignedInBrowser,
            new("Bank.Web", "showcase (Interactive Server)", "live evaluation over the circuit",
                [
                    AuthN("The PDP is anonymous — no token is forwarded; the scenario models an agent (client-credentials) Actor acting for the human."),
                ]),
            new("Authz PDP", "policy decision point", "evaluate the SAME action twice: human (Actor = null) vs agent (Actor set)",
                [
                    AuthZ("Delegation not impersonation: the agent is limited to the intersection of the human's rights and its delegated agent.bank.* scopes; the human path is unaffected and every decision records the acting agent."),
                ]),
        ]);
}
