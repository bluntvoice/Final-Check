using System.Text.Json;
using FinalCheck.Core.Abstractions;
using FinalCheck.Core.Comparisons;

namespace FinalCheck.Comparison;

public sealed class JsonComparisonResultSerializer : IComparisonResultSerializer
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.General)
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public byte[] Serialize(ComparisonResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.ComparisonSchemaVersion != ComparisonResult.CurrentSchemaVersion)
        {
            throw new NotSupportedException(
                $"Only comparison schema {ComparisonResult.CurrentSchemaVersion} can be serialized.");
        }

        return JsonSerializer.SerializeToUtf8Bytes(result, SerializerOptions);
    }

    public ComparisonResult Deserialize(ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty)
        {
            throw new InvalidDataException("The comparison payload is empty.");
        }

        try
        {
            using var document = JsonDocument.Parse(payload.ToArray());
            if (!document.RootElement.TryGetProperty("comparisonSchemaVersion", out var versionProperty) ||
                !versionProperty.TryGetInt32(out var schemaVersion))
            {
                throw new InvalidDataException("The comparison payload has no valid schema version.");
            }

            if (schemaVersion != ComparisonResult.CurrentSchemaVersion)
            {
                throw new NotSupportedException(
                    $"Comparison schema {schemaVersion} is not supported; expected {ComparisonResult.CurrentSchemaVersion}.");
            }

            return JsonSerializer.Deserialize<ComparisonResult>(payload, SerializerOptions)
                ?? throw new InvalidDataException("The comparison payload is invalid.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The comparison payload is not valid JSON.", exception);
        }
    }
}
