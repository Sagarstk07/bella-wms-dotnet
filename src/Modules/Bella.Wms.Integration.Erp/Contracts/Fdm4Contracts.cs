using System.Text.Json.Serialization;

namespace Bella.Wms.Integration.Erp.Contracts;

/// <summary>
/// The envelope every outbound message to FDM4 is wrapped in.
/// </summary>
/// <remarks>
/// <para>
/// Direct conversion of <c>api/wms/interface/tofdm4.cls</c> <c>buildRequest</c>
/// (lines 154-167). The whole envelope is fourteen lines of ABL and is entirely
/// schema-independent, which is why it converts now.
/// </para>
/// </remarks>
public sealed record Fdm4Request
{
    [JsonPropertyName("header")]
    public required Fdm4Header Header { get; init; }

    [JsonPropertyName("fileContent")]
    public required Fdm4FileContent FileContent { get; init; }
}

/// <summary>Header block. <c>tofdm4.cls:158-163</c>.</summary>
public sealed record Fdm4Header
{
    /// <summary>
    /// The file name, with any <c>.work</c> suffix stripped
    /// (<c>tofdm4.cls:218</c>). FDM4 correlates on this.
    /// </summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("company")]
    public required string Company { get; init; }

    [JsonPropertyName("warehouse")]
    public required string Warehouse { get; init; }

    /// <summary>Always <c>"WMS"</c>. <c>tofdm4.cls:161</c>.</summary>
    [JsonPropertyName("sourceSystem")]
    public string SourceSystem { get; init; } = "WMS";

    /// <summary>
    /// Timestamp in the exact ABL format
    /// <c>"9999-99-99THH:MM:SS+HH:MM"</c> (<c>tofdm4.cls:162</c>) — local time with
    /// offset, second precision, no fractional seconds.
    /// </summary>
    /// <remarks>
    /// Kept as a <see cref="string"/> and formatted explicitly by
    /// <see cref="Create"/>. Letting <c>System.Text.Json</c> serialise a
    /// <see cref="DateTimeOffset"/> would emit ISO-8601 with fractional seconds, which is
    /// a different string on the wire — and whether FDM4's parser tolerates that is
    /// unknown.
    /// </remarks>
    [JsonPropertyName("sentAt")]
    public required string SentAt { get; init; }

    /// <summary>Always <c>"impproc"</c>. <c>tofdm4.cls:163</c>.</summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = "impproc";

    /// <summary>Builds a header with <see cref="SentAt"/> in the ABL's exact format.</summary>
    public static Fdm4Header Create(string id, string company, string warehouse, DateTimeOffset? sentAt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(company);
        ArgumentException.ThrowIfNullOrWhiteSpace(warehouse);

        var stamp = sentAt ?? DateTimeOffset.Now;

        return new Fdm4Header
        {
            // ABL "9999-99-99THH:MM:SS+HH:MM" -> "yyyy-MM-ddTHH:mm:sszzz".
            Id = id,
            Company = company,
            Warehouse = warehouse,
            SentAt = stamp.ToString("yyyy-MM-ddTHH:mm:sszzz", System.Globalization.CultureInfo.InvariantCulture),
        };
    }
}

/// <summary>
/// Payload wrapper. <c>tofdm4.cls:164</c>.
/// </summary>
/// <remarks>
/// The processor's built payload goes in <see cref="Content"/> as a single value. The ABL
/// passes a <c>LONGCHAR</c>, so this is a string rather than a nested object — the inner
/// structure is opaque to the envelope.
/// </remarks>
public sealed record Fdm4FileContent
{
    [JsonPropertyName("content")]
    public required string Content { get; init; }
}
