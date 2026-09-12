using System.Text.Json.Nodes;

namespace ShiftSoftware.ShiftIdentity.Data.Services;

/// <summary>
/// Compares two access-tree JSON documents by meaning, not by text. Object keys may appear in any order, and an
/// array of access letters is a set. The generated tree is rebuilt on every save, so a text comparison would report
/// a change whenever only the formatting differs.
/// </summary>
public static class AccessTreeComparison
{
    public static bool Equivalent(string? first, string? second)
    {
        if (string.IsNullOrWhiteSpace(first) && string.IsNullOrWhiteSpace(second)) return true;
        JsonNode? a, b;
        try
        {
            a = string.IsNullOrWhiteSpace(first) ? null : JsonNode.Parse(first);
            b = string.IsNullOrWhiteSpace(second) ? null : JsonNode.Parse(second);
        }
        catch (System.Text.Json.JsonException)
        {
            // A value that is not valid JSON is compared as text; a rewrite to valid JSON is then a change.
            return string.Equals(first, second, StringComparison.Ordinal);
        }
        return Same(a, b);
    }

    private static bool Same(JsonNode? a, JsonNode? b)
    {
        if (a is null || b is null) return a is null && b is null;
        switch (a)
        {
            case JsonObject objectA when b is JsonObject objectB:
                if (objectA.Count != objectB.Count) return false;
                foreach (var (key, value) in objectA)
                {
                    if (!objectB.TryGetPropertyValue(key, out var other) || !Same(value, other)) return false;
                }
                return true;
            case JsonArray arrayA when b is JsonArray arrayB:
                if (arrayA.Count != arrayB.Count) return false;
                if (arrayA.All(x => x is JsonValue) && arrayB.All(x => x is JsonValue))
                {
                    var left = arrayA.Select(x => x!.ToJsonString()).Order(StringComparer.Ordinal);
                    var right = arrayB.Select(x => x!.ToJsonString()).Order(StringComparer.Ordinal);
                    return left.SequenceEqual(right, StringComparer.Ordinal);
                }
                return arrayA.Zip(arrayB).All(pair => Same(pair.First, pair.Second));
            case JsonValue when b is JsonValue:
                return string.Equals(a.ToJsonString(), b.ToJsonString(), StringComparison.Ordinal);
            default:
                return false;
        }
    }
}
