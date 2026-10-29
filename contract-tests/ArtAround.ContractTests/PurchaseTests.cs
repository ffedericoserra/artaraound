using System.Net;
using static ArtAround.ContractTests.TestData;

namespace ArtAround.ContractTests;

/*
 * POST /api/items/:id/purchase - Buy an item with wallet credits (auth)
 *
 * New users start with 100 credits. Each test registers its own buyer, because every
 * purchase changes the buyer's balance and purchase list.
 */

/// <summary>A seller with one cheap public item, one too expensive and one private.</summary>
public class SellerFixture : IAsyncLifetime
{
    public const int CheapPrice = 30;
    public const int ExpensivePrice = 500;

    public string SellerToken { get; private set; } = "";
    public string CheapItemId { get; private set; } = "";
    public string ExpensiveItemId { get; private set; } = "";
    public string PrivateItemId { get; private set; } = "";

    public async Task InitializeAsync()
    {
        SellerToken = await Api.RegisterAndGetTokenAsync(UniqueUsername(12));
        var (_, artworks) = await Resources.SeededMuseumAsync();
        CheapItemId = await Resources.CreateItemAsync(SellerToken, artworks[0], isPublic: true, CheapPrice);
        ExpensiveItemId = await Resources.CreateItemAsync(SellerToken, artworks[0], isPublic: true, ExpensivePrice);
        PrivateItemId = await Resources.CreateItemAsync(SellerToken, artworks[0], isPublic: false, CheapPrice);
    }

    public async Task DisposeAsync()
    {
        await Resources.HideItemAsync(SellerToken, CheapItemId);
        await Resources.HideItemAsync(SellerToken, ExpensiveItemId);
    }
}

public class PurchaseTests(SellerFixture seller) : IClassFixture<SellerFixture>
{
    private static Task<HttpResponseMessage> PurchaseAsync(string token, string itemId) =>
        Api.SendAsync(HttpMethod.Post, $"{Resources.ItemsApi}/{itemId}/purchase", token);

    [Fact]
    public async Task Purchase_AffordableItem_ChargesBuyer()
    {
        var buyer = await Api.RegisterAndGetTokenAsync(UniqueUsername(12));

        using var response = await PurchaseAsync(buyer, seller.CheapItemId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await Api.ReadJsonAsync(response);
        Assert.Equal(SellerFixture.CheapPrice, body.GetProperty("chargedAmount").GetInt32());
        Assert.Equal(100 - SellerFixture.CheapPrice, body.GetProperty("walletBalance").GetInt32());
        Assert.Equal(seller.CheapItemId, body.GetProperty("adoptedItemIds")[0].GetString());
    }

    [Fact]
    public async Task Purchase_AlreadyOwned_ChargesNothing()
    {
        var buyer = await Api.RegisterAndGetTokenAsync(UniqueUsername(12));
        using var first = await PurchaseAsync(buyer, seller.CheapItemId);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        using var second = await PurchaseAsync(buyer, seller.CheapItemId);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var body = await Api.ReadJsonAsync(second);
        Assert.Equal(0, body.GetProperty("chargedAmount").GetInt32());
        Assert.Equal(0, body.GetProperty("adoptedItemIds").GetArrayLength());
        Assert.Equal(100 - SellerFixture.CheapPrice, await Resources.WalletBalanceAsync(buyer));
    }

    [Fact]
    public async Task Purchase_ItemMadePurchasedReadable_AsBuyer()
    {
        var buyer = await Api.RegisterAndGetTokenAsync(UniqueUsername(12));
        using var purchase = await PurchaseAsync(buyer, seller.CheapItemId);
        Assert.Equal(HttpStatusCode.OK, purchase.StatusCode);

        using var response = await Api.SendAsync(HttpMethod.Get, $"{Resources.ItemsApi}/{seller.CheapItemId}", buyer);

        var item = (await Api.ReadJsonAsync(response)).GetProperty("item");
        Assert.True(item.GetProperty("isPurchased").GetBoolean());
        Assert.Equal(0, item.GetProperty("adoptionPrice").GetInt32());
    }

    [Fact]
    public async Task Purchase_TooExpensive_Returns400AndKeepsBalance()
    {
        var buyer = await Api.RegisterAndGetTokenAsync(UniqueUsername(12));

        using var response = await PurchaseAsync(buyer, seller.ExpensiveItemId);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Insufficient balance", (await Api.ReadJsonAsync(response)).GetProperty("error").GetString());
        Assert.Equal(100, await Resources.WalletBalanceAsync(buyer));
    }

    [Fact]
    public async Task Purchase_PrivateItem_Returns403()
    {
        var buyer = await Api.RegisterAndGetTokenAsync(UniqueUsername(12));

        using var response = await PurchaseAsync(buyer, seller.PrivateItemId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(100, await Resources.WalletBalanceAsync(buyer));
    }

    [Fact]
    public async Task Purchase_OwnItem_Returns400()
    {
        using var response = await PurchaseAsync(seller.SellerToken, seller.CheapItemId);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Purchase_WithoutToken_Returns401()
    {
        using var response = await PurchaseAsync(token: null!, seller.CheapItemId);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
