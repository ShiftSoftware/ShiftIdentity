using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShiftSoftware.ShiftIdentity.Data.Seeding;

/// <summary>
/// Reads a seed from JSON of exactly the <see cref="IdentitySeedData"/> shape: names in any case, enums by name, and
/// an unknown property is an error. An access tree is a JSON object or a JSON string.
/// </summary>
public static class IdentitySeedJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <exception cref="IdentitySeedException">The JSON does not parse into a seed.</exception>
    public static IdentitySeedData Read(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            return JsonSerializer.Deserialize<IdentitySeedData>(json, Options)
                ?? throw new IdentitySeedException(["The seed JSON is null."]);
        }
        catch (JsonException e)
        {
            throw new IdentitySeedException([$"The seed JSON is not valid: {e.Message}"], e);
        }
    }

    /// <summary>A JSON value kept as JSON text: an object or array is written out compactly, a string is taken as the JSON itself.</summary>
    public sealed class ObjectOrStringConverter : JsonConverter<string?>
    {
        public override bool HandleNull => true;

        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.TokenType switch
        {
            JsonTokenType.Null => null,
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.StartObject or JsonTokenType.StartArray => JsonSerializer.Serialize(JsonElement.ParseValue(ref reader)),
            _ => throw new JsonException($"Expected a JSON object or a JSON string, found {reader.TokenType}."),
        };

        public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
        {
            if (value is null) writer.WriteNullValue();
            else writer.WriteStringValue(value);
        }
    }
}

/// <summary>The seed was not applied: it does not parse, or it names something that is not there. Nothing was written.</summary>
public sealed class IdentitySeedException(IReadOnlyList<string> problems, Exception? inner = null)
    : Exception("The identity seed was not applied; nothing was written:" + Environment.NewLine + string.Join(Environment.NewLine, problems.Select(x => "- " + x)), inner)
{
    public IReadOnlyList<string> Problems { get; } = problems;
}

/// <summary>What one seed call did: rows inserted and updated per table, or nothing because the database is not SQLite.</summary>
public sealed class IdentitySeedResult
{
    /// <summary>The database is not SQLite, so nothing was seeded (a warning was logged).</summary>
    public bool Skipped { get; init; }

    /// <summary>Per table (the seed's list name, for example <c>Users</c>): rows inserted and rows updated. Link rows are not counted.</summary>
    public IReadOnlyDictionary<string, IdentitySeedTableCount> Tables { get; init; } = new Dictionary<string, IdentitySeedTableCount>();

    public int Inserted => Tables.Values.Sum(x => x.Inserted);
    public int Updated => Tables.Values.Sum(x => x.Updated);
}

public readonly record struct IdentitySeedTableCount(int Inserted, int Updated);
