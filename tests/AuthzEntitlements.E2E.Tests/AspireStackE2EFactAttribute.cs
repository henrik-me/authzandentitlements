using Xunit;

namespace AuthzEntitlements.E2E.Tests;

/// <summary>
/// CS57 — opt-in gate for the Aspire-stack e2e smoke test. xUnit 2.9.3 (v2) has no
/// runtime <c>Assert.Skip</c>, so a true, honestly-reported "skipped" test is expressed
/// as a <see cref="FactAttribute"/> subclass that sets <see cref="FactAttribute.Skip"/>
/// unless the opt-in environment variable is set. With <c>RUN_ASPIRE_E2E</c> unset (the
/// default for the fast local loop and the Docker-free CI <c>build-test</c> job), the
/// heavy Docker-backed e2e is reported <em>Skipped</em> rather than run; setting
/// <c>RUN_ASPIRE_E2E=1</c> (Docker running, no active <c>aspire run</c> holding port 8088)
/// runs it. No new package is introduced.
/// </summary>
public sealed class AspireStackE2EFactAttribute : FactAttribute
{
    public AspireStackE2EFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("RUN_ASPIRE_E2E") != "1")
        {
            Skip = "Set RUN_ASPIRE_E2E=1 (Docker running, no active aspire run on :8088) to run the Aspire stack e2e smoke.";
        }
    }
}
