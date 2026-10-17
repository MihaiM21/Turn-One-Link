using System;
using System.Collections.Generic;

namespace Turn_One_Link.Services.Telemetry;

/// <summary>
/// ACC's shared memory exposes no track length (unlike AC's <c>TrackSPlineLength</c>, which ACC
/// usually reports as 0). This static table maps ACC's <c>graphics.track</c> id to metres so
/// <see cref="T1SessionInfo.TrackLengthM"/> can be populated on session_start; the backend
/// self-calibrates from the first full lap when an id is missing (see backend_contract_v2.md §2).
/// </summary>
public static class AccTrackLengths
{
    public static readonly Dictionary<string, float> Metres = new(StringComparer.OrdinalIgnoreCase)
    {
        ["monza"] = 5793f,
        ["zolder"] = 4011f,
        ["brands_hatch"] = 3908f,
        ["silverstone"] = 5891f,
        ["paul_ricard"] = 5842f,
        ["misano"] = 4226f,
        ["spa"] = 7004f,
        ["nurburgring"] = 5137f,
        ["barcelona"] = 4655f,
        ["hungaroring"] = 4381f,
        ["zandvoort"] = 4252f,
        ["kyalami"] = 4522f,
        ["mount_panorama"] = 6213f,
        ["suzuka"] = 5807f,
        ["laguna_seca"] = 3602f,
        ["imola"] = 4909f,
        ["oulton_park"] = 4307f,
        ["donington"] = 4020f,
        ["snetterton"] = 4779f,
        ["cota"] = 5513f,
        ["indianapolis"] = 3925f,
        ["watkins_glen"] = 5552f,
        ["valencia"] = 4005f,
        ["red_bull_ring"] = 4318f,
        ["nurburgring_24h"] = 25378f,
        // The cheatsheet list above is ACC's full current roster as far as this table can
        // verify; unlisted/new DLC tracks fall through to 0 (backend self-calibrates).
    };

    /// <summary>Returns the track length in metres, or 0 when the id isn't in the table
    /// (the backend then self-calibrates from the car's first full lap).</summary>
    public static float Lookup(string? trackId) =>
        !string.IsNullOrEmpty(trackId) && Metres.TryGetValue(trackId, out var m) ? m : 0f;
}
