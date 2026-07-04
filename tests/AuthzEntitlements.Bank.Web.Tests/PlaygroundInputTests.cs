using AuthzEntitlements.Bank.Web.ViewModels;
using Xunit;

namespace AuthzEntitlements.Bank.Web.Tests;

public class PlaygroundInputTests
{
    [Fact]
    public void ToRequestDto_splits_roles_csv_into_trimmed_array()
    {
        var input = new PlaygroundInput
        {
            SubjectId = "user-teller1",
            Roles = "Teller, BranchManager ,  ComplianceOfficer",
            Action = "bank.account.read",
            ResourceType = "account",
        };

        var dto = input.ToRequestDto();

        Assert.Equal(["Teller", "BranchManager", "ComplianceOfficer"], dto.Subject.Roles);
    }

    [Fact]
    public void ToRequestDto_splits_scopes_csv_and_drops_blank_entries()
    {
        var input = new PlaygroundInput
        {
            SubjectId = "user-teller1",
            Action = "bank.transaction.create",
            ResourceType = "transaction",
            Scopes = "bank.read, ,bank.transactions.write,",
        };

        var dto = input.ToRequestDto();

        Assert.Equal(["bank.read", "bank.transactions.write"], dto.Context.Scopes);
    }

    [Fact]
    public void ToRequestDto_empty_roles_and_scopes_map_to_empty_arrays()
    {
        var input = new PlaygroundInput
        {
            SubjectId = "user-teller1",
            Action = "bank.account.read",
            ResourceType = "account",
        };

        var dto = input.ToRequestDto();

        Assert.Empty(dto.Subject.Roles);
        Assert.Empty(dto.Context.Scopes);
    }

    [Fact]
    public void ToRequestDto_passes_amount_maker_and_status_through()
    {
        var input = new PlaygroundInput
        {
            SubjectId = "user-teller1",
            Action = "bank.transaction.create",
            ResourceType = "transaction",
            Amount = 15_000m,
            MakerId = "user-teller1",
            Status = "Pending",
        };

        var dto = input.ToRequestDto();

        Assert.Equal(15_000m, dto.Resource.Amount);
        Assert.Equal("user-teller1", dto.Resource.MakerId);
        Assert.Equal("Pending", dto.Resource.Status);
    }

    [Fact]
    public void ToRequestDto_blank_optional_fields_map_to_null()
    {
        var input = new PlaygroundInput
        {
            SubjectId = "user-teller1",
            Action = "bank.account.read",
            ResourceType = "account",
            Tenant = "   ",
            Branch = "",
            ResourceId = null,
            MakerId = " ",
            Status = "",
        };

        var dto = input.ToRequestDto();

        Assert.Null(dto.Subject.Tenant);
        Assert.Null(dto.Subject.Branch);
        Assert.Null(dto.Resource.Id);
        Assert.Null(dto.Resource.Tenant);
        Assert.Null(dto.Resource.MakerId);
        Assert.Null(dto.Resource.Status);
        Assert.Null(dto.Resource.Amount);
    }

    [Fact]
    public void ToRequestDto_tenant_and_branch_flow_to_subject_and_resource()
    {
        var input = new PlaygroundInput
        {
            SubjectId = "user-teller1",
            Action = "bank.account.read",
            ResourceType = "account",
            Tenant = "CONTOSO",
            Branch = "NM01",
        };

        var dto = input.ToRequestDto();

        Assert.Equal("CONTOSO", dto.Subject.Tenant);
        Assert.Equal("NM01", dto.Subject.Branch);
        Assert.Equal("CONTOSO", dto.Resource.Tenant);
        Assert.Equal("NM01", dto.Resource.Branch);
    }

    [Fact]
    public void ToRequestDto_defaults_subject_type_to_user()
    {
        var input = new PlaygroundInput
        {
            SubjectType = "   ",
            SubjectId = "user-teller1",
            Action = "bank.account.read",
            ResourceType = "account",
        };

        var dto = input.ToRequestDto();

        Assert.Equal("user", dto.Subject.Type);
    }

    [Fact]
    public void ToRequestDto_maps_action_and_resource_type_verbatim_trimmed()
    {
        var input = new PlaygroundInput
        {
            SubjectId = "user-teller1",
            Action = "  bank.transaction.approve ",
            ResourceType = " transaction ",
        };

        var dto = input.ToRequestDto();

        Assert.Equal("bank.transaction.approve", dto.Action.Name);
        Assert.Equal("transaction", dto.Resource.Type);
    }

    [Fact]
    public void ToRequestDto_resource_tenant_overrides_subject_tenant_for_cross_tenant_request()
    {
        var input = new PlaygroundInput
        {
            SubjectId = "user-teller1",
            Roles = "Teller",
            Action = "bank.account.read",
            ResourceType = "account",
            Tenant = "CONTOSO",
            ResourceTenant = "FABRIKAM",
        };

        var dto = input.ToRequestDto();

        Assert.Equal("CONTOSO", dto.Subject.Tenant);
        Assert.Equal("FABRIKAM", dto.Resource.Tenant);
    }

    [Fact]
    public void ToRequestDto_blank_resource_tenant_falls_back_to_subject_tenant()
    {
        var input = new PlaygroundInput
        {
            SubjectId = "user-teller1",
            Action = "bank.account.read",
            ResourceType = "account",
            Tenant = "CONTOSO",
            ResourceTenant = "   ",
        };

        var dto = input.ToRequestDto();

        Assert.Equal("CONTOSO", dto.Subject.Tenant);
        Assert.Equal("CONTOSO", dto.Resource.Tenant);
    }
}
