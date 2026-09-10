using System.Text.Json;
using FinalCheck.Core.Abstractions;
using FinalCheck.Core.Documents;

namespace FinalCheck.Documents;

public sealed class JsonDocumentSnapshotSerializer : IDocumentSnapshotSerializer
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.General)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public byte[] Serialize(DocumentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return JsonSerializer.SerializeToUtf8Bytes(snapshot, SerializerOptions);
    }

    public DocumentSnapshot Deserialize(ReadOnlySpan<byte> payload)
    {
        var snapshot = JsonSerializer.Deserialize<DocumentSnapshot>(payload, SerializerOptions)
            ?? throw new InvalidDataException("The snapshot payload is empty or invalid.");
        if (snapshot.SnapshotSchemaVersion > DocumentSnapshot.CurrentSchemaVersion)
        {
            throw new NotSupportedException(
                $"Snapshot schema {snapshot.SnapshotSchemaVersion} is newer than supported schema {DocumentSnapshot.CurrentSchemaVersion}.");
        }

        return snapshot;
    }
}
