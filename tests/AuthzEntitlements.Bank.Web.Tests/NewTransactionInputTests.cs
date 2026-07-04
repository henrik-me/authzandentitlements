using System.ComponentModel.DataAnnotations;
using AuthzEntitlements.Bank.Web.Clients;
using AuthzEntitlements.Bank.Web.ViewModels;
using Xunit;

namespace AuthzEntitlements.Bank.Web.Tests;

// Offline unit tests for the MAKER create-transaction form model. No server, Docker, or
// Keycloak required — they exercise the pure field-to-request mapping and the
// DataAnnotations rules that guard the write (fail-closed: positive amount, chosen
// account).
public class NewTransactionInputTests
{
    private static bool Validate(NewTransactionInput input, out List<ValidationResult> results)
    {
        results = [];
        var context = new ValidationContext(input);
        return Validator.TryValidateObject(input, context, results, validateAllProperties: true);
    }

    [Fact]
    public void ToRequest_maps_all_fields_with_the_supplied_maker_id()
    {
        var accountId = new Guid("50000000-0000-0000-0000-000000000001");
        var makerId = new Guid("40000000-0000-0000-0000-000000000001");
        var input = new NewTransactionInput
        {
            AccountId = accountId,
            Type = TransactionType.Transfer,
            Amount = 1234.56m,
            Reference = "Wire out",
        };

        var request = input.ToRequest(makerId);

        Assert.Equal(accountId, request.AccountId);
        Assert.Equal(TransactionType.Transfer, request.Type);
        Assert.Equal(1234.56m, request.Amount);
        Assert.Equal(makerId, request.MakerId);
        Assert.Equal("Wire out", request.Reference);
    }

    [Fact]
    public void Type_defaults_to_debit()
    {
        Assert.Equal(TransactionType.Debit, new NewTransactionInput().Type);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ToRequest_normalizes_blank_reference_to_null(string? reference)
    {
        var input = new NewTransactionInput
        {
            AccountId = new Guid("50000000-0000-0000-0000-000000000001"),
            Amount = 10m,
            Reference = reference,
        };

        Assert.Null(input.ToRequest(Guid.NewGuid()).Reference);
    }

    [Fact]
    public void ToRequest_trims_surrounding_whitespace_from_reference()
    {
        var input = new NewTransactionInput
        {
            AccountId = new Guid("50000000-0000-0000-0000-000000000001"),
            Amount = 10m,
            Reference = "  ATM withdrawal  ",
        };

        Assert.Equal("ATM withdrawal", input.ToRequest(Guid.NewGuid()).Reference);
    }

    [Fact]
    public void Validation_passes_for_a_well_formed_input()
    {
        var input = new NewTransactionInput
        {
            AccountId = new Guid("50000000-0000-0000-0000-000000000001"),
            Type = TransactionType.Credit,
            Amount = 250.00m,
            Reference = "Deposit",
        };

        Assert.True(Validate(input, out var results));
        Assert.Empty(results);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Validation_flags_a_non_positive_amount(decimal amount)
    {
        var input = new NewTransactionInput
        {
            AccountId = new Guid("50000000-0000-0000-0000-000000000001"),
            Amount = amount,
        };

        Assert.False(Validate(input, out var results));
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(NewTransactionInput.Amount)));
    }

    [Fact]
    public void Validation_flags_an_empty_account_id()
    {
        var input = new NewTransactionInput
        {
            AccountId = Guid.Empty,
            Amount = 100m,
        };

        Assert.False(Validate(input, out var results));
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(NewTransactionInput.AccountId)));
    }
}
