namespace AuthzEntitlements.Bank.Api.Domain;

// Raised when a maker attempts to approve their own transaction. Segregation of
// duties requires the checker to be a different person from the maker; this is a
// hard domain invariant, not a policy toggle, so it is expressed as a dedicated
// exception the endpoint layer can translate into a 409/422 response.
public sealed class SegregationOfDutiesViolationException : Exception
{
    public Guid MakerId { get; }
    public Guid TransactionId { get; }

    public SegregationOfDutiesViolationException(Guid makerId, Guid transactionId)
        : base($"Segregation-of-duties violation: maker {makerId} may not approve " +
               $"their own transaction {transactionId}.")
    {
        MakerId = makerId;
        TransactionId = transactionId;
    }
}
