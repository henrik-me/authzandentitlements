using System.ComponentModel.DataAnnotations;
using AuthzEntitlements.Bank.Web.Clients;

namespace AuthzEntitlements.Bank.Web.ViewModels;

// Form model for the MAKER create-transaction page. Kept pure and dependency-free so
// the field-to-request mapping and DataAnnotations rules are unit-testable offline.
// The MakerId is NEVER a form field: it is bound at submit time from the resolved token
// identity (fail-closed authz — a caller may not act as another subject), so ToRequest
// takes it as an explicit argument rather than reading it from user input.
public sealed class NewTransactionInput
{
    [NonDefaultGuid(ErrorMessage = "Select an account.")]
    public Guid AccountId { get; set; }

    public TransactionType Type { get; set; } = TransactionType.Debit;

    [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be positive.")]
    public decimal Amount { get; set; }

    public string? Reference { get; set; }

    public CreateTransactionRequest ToRequest(Guid makerId) =>
        new(AccountId, Type, Amount, makerId,
            string.IsNullOrWhiteSpace(Reference) ? null : Reference.Trim());
}

// Validates that a Guid property is not the default (empty) value. A plain [Required] on
// a non-nullable Guid never fails because a value type is always "present", so an empty
// account selection would slip through — this fails closed instead.
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class NonDefaultGuidAttribute : ValidationAttribute
{
    public override bool IsValid(object? value) =>
        value is Guid guid && guid != Guid.Empty;
}
