using System.Text.Json;
using System.Text.Json.Serialization;

namespace ArtAround.Api.Infrastructure;

public static class JsonConventions
{
    /// <summary>The JSON settings shared by controllers and the SignalR hub.</summary>
    public static void Apply(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        options.Converters.Add(new IdConverter());
    }
}

/// <summary>
/// Convention: in this API every <c>long</c> is an id, and ids are JSON strings.
/// The frontends were written against Mongo's string ObjectIds and compare ids as
/// strings, and a JavaScript number cannot hold every bigint. Reading accepts both a
/// string and a number. Counts and prices are <c>int</c>, so they are unaffected.
/// </summary>
public class IdConverter : JsonConverter<long>
{
    public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out var number))
        {
            return number;
        }
        if (reader.TokenType == JsonTokenType.String && long.TryParse(reader.GetString(), out var parsed))
        {
            return parsed;
        }
        throw new JsonException("Invalid id format");
    }

    public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}

/// <summary>
/// Trims a string while it is deserialized, so validation sees what will be stored:
/// "  ab  " is a 2-character username. Opt-in per property; passwords are never trimmed.
/// </summary>
public class TrimmedStringConverter : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String ? reader.GetString()!.Trim() : throw new JsonException();

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);
}
