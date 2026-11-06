using System.Text.Json;
using System.Text.Json.Serialization;

namespace Turn_One_Link.Services.Telemetry;

/// <summary>
/// The cloud protocol v2 envelope <c>{ v: 2, type, timestamp, sessionId, data }</c>
/// (docs/telemetry/backend_contract_v2.md §1), shared by the live stream and the lap upload files so
/// both send byte-identical frames.
/// </summary>
public static class V2Envelope
{
    /// <summary>camelCase, omit-null optionals, T1Corners as [fl, fr, rl, rr].</summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new T1CornersJsonConverter() }
    };

    public static string Serialize<T>(string type, T data, string? sessionId, long? timestamp = null) =>
        JsonSerializer.Serialize(new
        {
            v = 2,
            type,
            timestamp = timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            sessionId,
            data
        }, JsonOptions);

    /// <summary>The <c>session_start</c> payload built from a source's session info.</summary>
    public static object SessionStartData(T1SessionInfo info, DateTimeOffset startedAt) => new
    {
        source = info.Source,
        simVersion = info.SimVersion,
        trackId = info.TrackId,
        trackName = info.TrackName,
        trackLengthM = info.TrackLengthM,
        sectorCount = info.SectorCount,
        sectorBoundariesM = info.SectorBoundariesM,
        car = new { id = info.CarId, name = info.CarName, @class = info.CarClass },
        sessionType = T1Wire.SessionTypeName(info.SessionType),
        sessionTypeRaw = info.SessionTypeRaw,
        driver = info.Driver,
        tickRateHz = info.TickRateHz,
        startedAt = startedAt.ToUnixTimeMilliseconds()
    };
}
