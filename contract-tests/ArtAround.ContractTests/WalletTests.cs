using System.Net;
using static ArtAround.ContractTests.TestData;

namespace ArtAround.ContractTests;

/*
 * PATCH /api/auth/wallet - Recharge the caller's wallet (auth)
 *
 * Every test registers its own user: they all change the balance, so sharing one
 * user would make each test depend on the others' order.
 */
public class WalletTests
{
    private const string WalletApi = "/api/auth/wallet";
    private const int StartingBalance = 100;

    [Fact]
    public async Task Recharge_ValidAmount_AddsToBalance()
    {
        var token = await Api.RegisterAndGetTokenAsync(UniqueUsername(12));

        using var response = await Api.SendAsync(HttpMethod.Patch, WalletApi, token, new { amount = 50 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await Api.ReadJsonAsync(response);
        Assert.Equal(StartingBalance + 50, body.GetProperty("walletBalance").GetInt32());
        Assert.Equal(StartingBalance + 50, await Resources.WalletBalanceAsync(token));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(10_001)]
    public async Task Recharge_AmountOutOfRange_Returns400AndKeepsBalance(int amount)
    {
        var token = await Api.RegisterAndGetTokenAsync(UniqueUsername(12));

        using var response = await Api.SendAsync(HttpMethod.Patch, WalletApi, token, new { amount });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(StartingBalance, await Resources.WalletBalanceAsync(token));
    }

    [Fact]
    public async Task Recharge_WithoutToken_Returns401()
    {
        using var response = await Api.SendAsync(HttpMethod.Patch, WalletApi, body: new { amount = 50 });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
