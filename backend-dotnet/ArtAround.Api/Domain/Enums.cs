using System.Text.Json.Serialization;
using NpgsqlTypes;

namespace ArtAround.Api.Domain;

// Each enum is a Postgres ENUM type (mapped in AppDbContext). [PgName] is the label in
// the database, [JsonStringEnumMemberName] the string in JSON. Members without
// attributes use the defaults: snake_case in Postgres, camelCase in JSON.

public enum Language { It, En }

public enum Tone { Easy, Medium, Complex }

public enum License
{
    [PgName("CC-BY"), JsonStringEnumMemberName("CC-BY")] CcBy,
    [PgName("CC-BY-SA"), JsonStringEnumMemberName("CC-BY-SA")] CcBySa,
    [PgName("CC-BY-NC"), JsonStringEnumMemberName("CC-BY-NC")] CcByNc,
    [PgName("Copyright"), JsonStringEnumMemberName("Copyright")] Copyright,
    [PgName("Public Domain"), JsonStringEnumMemberName("Public Domain")] PublicDomain,
}

public enum ContentType
{
    [PgName("Artwork"), JsonStringEnumMemberName("Artwork")] Artwork,
    [PgName("Artist"), JsonStringEnumMemberName("Artist")] Artist,
    [PgName("Movement"), JsonStringEnumMemberName("Movement")] Movement,
    [PgName("Place"), JsonStringEnumMemberName("Place")] Place,
}

public enum PoiType { Toilet, Exit, Bar, Stairs, Entrance, Shop }

public enum VisitType { Standard, Synchronized }

public enum VisitLength { Quick, Normal, Deep }

public enum BlockType { Artwork, Questions }

public enum AnswerType
{
    Open,
    [PgName("multiple-choice"), JsonStringEnumMemberName("multiple-choice")] MultipleChoice,
}

// 'joined' and 'left' are written by the server; the rest are navigator command ids.
// The legacy ids stay so old sessions still load.
public enum ActivityAction
{
    Joined, Left, More, Shorter, Complex, Simpler, Details, Map,
    Author, Year, Exit,
    [PgName("tellMore")] TellMore,
    [PgName("tellLess")] TellLess,
    [PgName("tooSimple")] TooSimple,
}
