namespace AuthzEntitlements.Authz.Pdp.Contracts;

// Capability marker (CS45): a provider implements this to DECLARE that it natively honours the
// CS19/CS21 EXTENDED-AUTHORIZATION context — on-behalf-of (`Subject.Actor`), manager->delegate
// delegation (`Context.Delegation`), and break-glass elevation (`Context.BreakGlass`). It is an
// intentionally EMPTY marker: it adds no members, only a compile-time + runtime signal.
//
// Why it exists: only engines that constrain the decision to the human/actor intersection, honour
// grant expiry, and enforce the delegation/break-glass invariants may be trusted with a request
// that carries any of those three fields. An engine that maps only the human subject would evaluate
// an on-behalf-of call by the human's rights alone and could PERMIT access the reference engine
// DENIES (e.g. the actor lacks the delegated scope) — a silent fail-OPEN on an engine swap.
//
// How it is enforced: `AuthorizationDecisionProviderFactory` wraps every provider that does NOT
// implement this marker in the fail-closed `ExtendedContextGuardProvider`, so any request carrying
// `Subject.Actor` / `Context.Delegation` / `Context.BreakGlass` denies with
// `ReasonCodes.ExtendedContextUnsupported` rather than being forwarded. Providers that DO implement
// it (currently only `ReferenceDecisionProvider`) pass through unwrapped and apply their own
// CS19/CS21 semantics. A future engine opts in the moment it natively honours the extended context —
// by implementing this interface, nothing else.
public interface ISupportsExtendedAuthorizationContext
{
}
