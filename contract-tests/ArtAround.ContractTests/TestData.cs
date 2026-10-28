using System.Text;

namespace ArtAround.ContractTests;

internal static class TestData
{
    internal const string RegisterApi = "/api/auth/register";
    internal const string TestPassword = "12345678";

    private const string Chars = "abcdefghijklmnopqrstuvwxyz0123456789";

    internal static string RandomString(int length)
    {
        var sb = new StringBuilder(length);
        for (int i = 0; i < length; i++)
        {
            sb.Append(Chars[Random.Shared.Next(Chars.Length)]);
        }

        return sb.ToString();
    }

    internal static string UniqueUsername(int length)
    {
        // The API requires at least 3 characters; "t_" alone would be invalid.
        ArgumentOutOfRangeException.ThrowIfLessThan(length, 3);
        return "t_" + RandomString(length - 2);
    }
}
