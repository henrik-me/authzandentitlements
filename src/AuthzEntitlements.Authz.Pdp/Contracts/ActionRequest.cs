namespace AuthzEntitlements.Authz.Pdp.Contracts;

// The AuthZEN "action" — what the subject wants to do. Named ActionRequest (not Action)
// to avoid clashing with System.Action. Name is one of the well-known verbs in
// ActionNames (e.g. "bank.transaction.approve").
public sealed record ActionRequest(string Name);
