using System.Net;
using System.Text.Json;
using static ArtAround.ContractTests.TestData;

namespace ArtAround.ContractTests;

/*
 * POST /api/auth/register - Create new user
 * POST /api/auth/login    - Login and get JWT
 * GET  /api/auth/me       - Get current user
 */

/// <summary>One registered user shared by every test in a class (JUnit's @BeforeAll).</summary>
public class RegisteredUserFixture : IAsyncLifetime
{
    public string Username { get; } = UniqueUsername(10);

    public async Task InitializeAsync() => await Api.RegisterAndGetTokenAsync(Username);

    // Users cannot be deleted through the API, so there is nothing to clean up.
    public Task DisposeAsync() => Task.CompletedTask;
}

public class AuthTests(RegisteredUserFixture user) : IClassFixture<RegisteredUserFixture>
{
    private const string LoginApi = "/api/auth/login";
    private const string MeApi = "/api/auth/me";

    // Register validation limits.
    private const int UsernameMin = 3;
    private const int UsernameMax = 30;
    private const int PasswordMin = 6;
    private const int PasswordMax = 100;

    /* ---------- helpers ---------- */

    private static Task<HttpResponseMessage> AttemptLoginAsync(string username, string password) =>
        Api.SendAsync(HttpMethod.Post, LoginApi, body: new { username, password });

    private async Task<string> LoginAndGetTokenAsync()
    {
        using var response = await AttemptLoginAsync(user.Username, TestPassword);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await Api.ReadJsonAsync(response);
        return body.GetProperty("token").GetString()!;
    }

    private static string UnknownUsername() => "nobody_" + RandomString(8);

    private static async Task AssertValidationErrorAsync(HttpResponseMessage response, string field)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await Api.ReadJsonAsync(response);
        Assert.Equal("Validation Error", body.GetProperty("error").GetString());
        // details[0].field proves the intended rule fired, not some other one.
        Assert.Equal(field, body.GetProperty("details")[0].GetProperty("field").GetString());
    }

    /* ---------- login ---------- */

    [Fact]
    public async Task Login_ValidCredentials_ReturnsToken()
    {
        using var response = await AttemptLoginAsync(user.Username, TestPassword);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await Api.ReadJsonAsync(response);
        Assert.False(string.IsNullOrEmpty(body.GetProperty("token").GetString()));
    }

    [Fact]
    public async Task Login_WrongPassword_Returns401WithoutToken()
    {
        using var response = await AttemptLoginAsync(user.Username, "wrong-password");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await Api.ReadJsonAsync(response);
        Assert.False(body.TryGetProperty("token", out _));
    }

    [Fact]
    public async Task Login_UnknownUser_Returns401WithoutToken()
    {
        using var response = await AttemptLoginAsync(UnknownUsername(), TestPassword);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await Api.ReadJsonAsync(response);
        Assert.False(body.TryGetProperty("token", out _));
    }

    // Identical responses prevent user enumeration.
    [Fact]
    public async Task Login_WrongPasswordAndUnknownUser_AreIndistinguishable()
    {
        using var wrongPassword = await AttemptLoginAsync(user.Username, "wrong-password");
        using var unknownUser = await AttemptLoginAsync(UnknownUsername(), TestPassword);

        Assert.Equal(wrongPassword.StatusCode, unknownUser.StatusCode);
        // Raw text equality: both bodies come from the same code path, so key order matches.
        Assert.Equal(
            await wrongPassword.Content.ReadAsStringAsync(),
            await unknownUser.Content.ReadAsStringAsync());
    }

    /* ---------- /me ---------- */

    [Fact]
    public async Task Me_WithToken_ReturnsCurrentUser()
    {
        var token = await LoginAndGetTokenAsync();

        using var response = await Api.SendAsync(HttpMethod.Get, MeApi, token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await Api.ReadJsonAsync(response);
        Assert.Equal(user.Username, body.GetProperty("user").GetProperty("username").GetString());
    }

    [Fact]
    public async Task Me_WithoutToken_Returns401()
    {
        using var response = await Api.SendAsync(HttpMethod.Get, MeApi);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_WithToken_NeverExposesPasswordHash()
    {
        var token = await LoginAndGetTokenAsync();

        using var response = await Api.SendAsync(HttpMethod.Get, MeApi, token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var userJson = (await Api.ReadJsonAsync(response)).GetProperty("user");
        Assert.Equal(JsonValueKind.Object, userJson.ValueKind);
        Assert.False(userJson.TryGetProperty("password", out _));
        Assert.False(userJson.TryGetProperty("password_hash", out _));
        Assert.False(userJson.TryGetProperty("passwordHash", out _));
    }

    /* ---------- register validation ---------- */

    // Rejected input never reaches the database, so any string of the right length works.
    [Theory]
    [InlineData(UsernameMin - 1)]
    [InlineData(UsernameMax + 1)]
    public async Task Register_UsernameLengthOutOfRange_Returns400(int length)
    {
        using var response = await Api.AttemptRegisterAsync(RandomString(length), TestPassword);

        await AssertValidationErrorAsync(response, "username");
    }

    // Short usernames run out quickly, so 409 (already taken) also proves the length passed
    // validation. This tests the validator, not registration.
    [Theory]
    [InlineData(UsernameMin)]
    [InlineData(UsernameMax)]
    public async Task Register_UsernameLengthAtLimit_IsAccepted(int length)
    {
        using var response = await Api.AttemptRegisterAsync(UniqueUsername(length), TestPassword);

        Assert.True(response.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict,
            $"Unexpected {response.StatusCode}");
    }

    // "  ab  " must be rejected: users never see surrounding spaces. Node quirk: Zod runs min(3)
    // before trim(), so Mongoose rejects it instead, with a different body ({ messages: [...] }
    // instead of { details: [...] }). Only the common part is asserted; .NET should return the
    // usual details[] shape.
    [Fact]
    public async Task Register_UsernameTooShortAfterTrim_Returns400()
    {
        using var response = await Api.SendAsync(HttpMethod.Post, RegisterApi, body: new
        {
            username = "  ab  ",
            password = TestPassword,
            email = $"{UnknownUsername()}@test.local",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await Api.ReadJsonAsync(response);
        Assert.Equal("Validation Error", body.GetProperty("error").GetString());
    }

    [Theory]
    [InlineData(PasswordMin - 1)]
    [InlineData(PasswordMax + 1)]
    public async Task Register_PasswordLengthOutOfRange_Returns400(int length)
    {
        using var response = await Api.AttemptRegisterAsync(UniqueUsername(12), RandomString(length));

        await AssertValidationErrorAsync(response, "password");
    }

    [Theory]
    [InlineData(PasswordMin)]
    [InlineData(PasswordMax)]
    public async Task Register_PasswordLengthAtLimit_Returns201(int length)
    {
        using var response = await Api.AttemptRegisterAsync(UniqueUsername(12), RandomString(length));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
