using static ArtAround.ContractTests.TestData;

namespace ArtAround.ContractTests;

public class TestDataTests
{
    [Fact]
    public void UniqueUsername_GivenLength_HasLengthAndPrefix()
    {
        string username = UniqueUsername(10);

        Assert.Equal(10, username.Length);
        Assert.StartsWith("t_", username);
    }

    [Fact]
    public void UniqueUsername_LengthBelowMinimum_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => UniqueUsername(2));
    }
}
