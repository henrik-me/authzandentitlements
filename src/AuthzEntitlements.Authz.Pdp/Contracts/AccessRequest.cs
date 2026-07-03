namespace AuthzEntitlements.Authz.Pdp.Contracts;

// The full AuthZEN Access Evaluation question: may this Subject perform this Action on
// this Resource in this Context? This is the single shape every engine adapter answers,
// so a scenario expressed once dispatches unchanged to any provider.
public sealed record AccessRequest(
    Subject Subject,
    ActionRequest Action,
    Resource Resource,
    EvaluationContext Context);
