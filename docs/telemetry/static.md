# Session Static Info (`type: "static"`)

This message is **sent once per session** (not repeated every frame). It contains metadata about the car, the track, and the event format.

## Schema Reference

| Field | Type | Description |
|---|---|---|
| `smVersion` | string | Shared memory protocol version |
| `acVersion` | string | Game version |
| `numberOfSessions` | int | Sessions in event |
| `numCars` | int | Cars in session |
| `carModel` | string | Car identifier (e.g. `"ferrari_488_gt3"`) |
| `track` | string | Track identifier (e.g. `"monza"`) |
| `playerName` | string | Driver first name |
| `playerSurname` | string | Driver surname |
| `playerNick` | string | Driver nickname |
| `sectorCount` | int | Number of track sectors |
| `maxRpm` | int | Engine rev limit |
| `maxFuel` | float | Fuel tank capacity (litres) |
| `maxTorque` | float | Peak engine torque (Nm) |
| `maxPower` | float | Peak engine power (W) |
| `maxTurboBoost` | float | Max turbo pressure |
| `suspensionMaxTravel` | float[4] | Max suspension travel [FL, FR, RL, RR] |
| `tyreRadius` | float[4] | Tyre radius per corner |
| `trackSPlineLength` | float | Total track length (meters) |
| `trackConfiguration` | string | Track layout variant |
| `penaltiesEnabled` | int | Penalty system active |
| `aidFuelRate` | float | Fuel consumption aid multiplier |
| `aidTireRate` | float | Tyre wear aid multiplier |
| `aidMechanicalDamage` | float | Damage aid multiplier |
| `aidAllowTyreBlankets` | int | Tyre blankets allowed |
| `aidStability` | float | Stability aid level |
| `aidAutoClutch` | int | Auto clutch aid |
| `aidAutoBlip` | int | Auto blip aid |
| `hasDRS` | int | Car has DRS |
| `hasERS` | int | Car has ERS |
| `hasKERS` | int | Car has KERS |
| `kersMaxJoules` | float | Max KERS energy |
| `engineBrakeSettingsCount` | int | Engine brake adjustment steps |
| `ersPowerControllerCount` | int | ERS power controller presets |
| `ersMaxJ` | float | Max ERS energy |
| `isTimedRace` | int | Timed vs. lapped race |
| `hasExtraLap` | int | Extra lap after timer |
| `carSkin` | string | Active car livery identifier |
| `reversedGridPositions` | int | Reversed grid positions |
| `pitWindowStart` | int | Pit window open lap/time |
| `pitWindowEnd` | int | Pit window close lap/time |
| `isOnline` | int | Online session flag |
| `dryTyresName` | string | Dry tyre compound name |
| `wetTyresName` | string | Wet tyre compound name |

## Static Ordering Note

The client emits `session_start` as soon as a new session is detected. If the static shared-memory block has already been read, `track` / `carModel` / `driver` are populated immediately. Otherwise:

1. `session_start` arrives with **empty strings** for `track` / `carModel` / `driver`.
2. A `static` frame for that `sessionId` arrives within ~5–10ms.
3. The client then emits a *second* `session_start` for the same `sessionId` with the populated fields.

Backend should: treat the latest `session_start` (or the dedicated `static` frame) as authoritative for session metadata. Both have the same `sessionId`, so this is an upsert, not a duplicate.
