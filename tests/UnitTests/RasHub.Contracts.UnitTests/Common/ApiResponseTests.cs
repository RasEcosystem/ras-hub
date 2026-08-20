using System.Net;
using System.Text.Json;
using RasHub.Contracts.Common;

namespace RasHub.Contracts.UnitTests.Common;

public sealed class ApiResponseTests
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    [Fact]
    public void Ok_creates_successful_response_with_data()
    {
        var response = ApiResponse<string>.Ok("value");

        Assert.True(response.Success);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("value", response.Data);
        Assert.Null(response.Error);
        Assert.Null(response.Errors);
    }

    [Fact]
    public void Created_creates_successful_response_with_created_status()
    {
        var response = ApiResponse<string>.Created("value");

        Assert.True(response.Success);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("value", response.Data);
        Assert.Null(response.Error);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "bad_request", "Bad request")]
    [InlineData(HttpStatusCode.Unauthorized, "unauthorized", "Unauthorized")]
    [InlineData(HttpStatusCode.Forbidden, "forbidden", "Access denied")]
    [InlineData(HttpStatusCode.NotFound, "not_found", "Resource not found")]
    [InlineData(HttpStatusCode.Conflict, "conflict", "Conflict")]
    [InlineData(HttpStatusCode.InternalServerError, "internal_error", "Unexpected server error")]
    [InlineData(HttpStatusCode.BadGateway, "request_failed", "Unexpected server error")]
    public void Fail_uses_the_expected_default_error(
        HttpStatusCode statusCode,
        string expectedCode,
        string expectedMessage)
    {
        var response = ApiResponse<object>.Fail(statusCode);

        Assert.False(response.Success);
        Assert.Equal(statusCode, response.StatusCode);
        Assert.Equal(expectedCode, response.Error?.Code);
        Assert.Equal(expectedMessage, response.Error?.Message);
        Assert.Null(response.Data);
    }

    [Fact]
    public void Fail_materializes_validation_errors()
    {
        var errors = new[]
        {
            new ApiError("validation_error", "First", "Name"), new ApiError("validation_error", "Second", "Port")
        };

        var response = ApiResponse<object>.Fail(HttpStatusCode.BadRequest, errors);

        Assert.False(response.Success);
        Assert.Equal("bad_request", response.Error?.Code);
        Assert.Equal(errors, response.Errors);
    }

    [Fact]
    public void Json_round_trip_success_response_preserves_wire_payload()
    {
        var response = ApiResponse<string>.Ok("value");

        var result = RoundTrip(response);

        Assert.True(result.Success);
        Assert.Equal("value", result.Data);
        Assert.Null(result.Error);
        Assert.Null(result.Errors);
    }

    [Fact]
    public void Json_round_trip_single_error_response_preserves_wire_payload()
    {
        var response = ApiResponse<object>.Fail(
            HttpStatusCode.Conflict,
            "resource_conflict",
            "Resource has changed");

        var result = RoundTrip(response);

        Assert.False(result.Success);
        Assert.Equal(
            new ApiError("resource_conflict", "Resource has changed"),
            result.Error);
        Assert.Null(result.Data);
        Assert.Null(result.Errors);
    }

    [Fact]
    public void Json_round_trip_validation_error_response_preserves_wire_payload()
    {
        var errors = new[]
        {
            new ApiError("validation_error", "Name is required", "Name"),
            new ApiError("validation_error", "Port is invalid", "Port")
        };
        var response = ApiResponse<object>.Fail(
            HttpStatusCode.BadRequest,
            errors);

        var result = RoundTrip(response);

        Assert.False(result.Success);
        Assert.Equal(new ApiError("bad_request", "Bad request"), result.Error);
        Assert.Equal(errors, result.Errors);
        Assert.Null(result.Data);
    }

    private static ApiResponse<T> RoundTrip<T>(ApiResponse<T> response)
    {
        var json = JsonSerializer.Serialize(response, SerializerOptions);

        Assert.DoesNotContain("statusCode", json, StringComparison.OrdinalIgnoreCase);

        return JsonSerializer.Deserialize<ApiResponse<T>>(json, SerializerOptions)!;
    }
}
