using AuthzEntitlements.Bank.Web.Clients;
using Xunit;

namespace AuthzEntitlements.Bank.Web.Tests;

public class ApiResultTests
{
    [Fact]
    public void Success_sets_value_and_status()
    {
        var result = ApiResult<string>.Success("ok", 201);

        Assert.True(result.IsSuccess);
        Assert.Equal(201, result.StatusCode);
        Assert.Equal("ok", result.Value);
        Assert.Null(result.Error);
    }

    [Theory]
    [InlineData(403, "forbidden")]
    [InlineData(409, "conflict")]
    [InlineData(503, "unavailable")]
    public void Failure_captures_status_and_error_without_value(int status, string error)
    {
        var result = ApiResult<string>.Failure(status, error);

        Assert.False(result.IsSuccess);
        Assert.Equal(status, result.StatusCode);
        Assert.Null(result.Value);
        Assert.Equal(error, result.Error);
    }
}
