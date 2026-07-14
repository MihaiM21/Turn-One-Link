# Session & Race State (`type: "graphics"`)

This message type is sent at a high frequency (~100Hz) when the game is running. It contains session state, timing, track information, and relative positions of all cars.

## Example Message

```json
{
  "type": "graphics",
  "timestamp": 1776764355843,
  "sessionId": "9f4a2c1e7b3d4f80a1c2e5d8f9a0b1c2",
  "data": {
    "packetId": 1144,
    "status": "AC_LIVE",
    "session": "AC_PRACTICE",
    "currentTime": "0:15:543",
    "iCurrentTime": 15543,
    "completedLaps": 0,
    "position": 1,
    "speedKmh": 0.0,
    "fuelXLap": 3.66,
    "fuelEstimatedLaps": 16.94,
    "rainIntensity": "NoRain",
    "trackGripStatus": "Fast",
    "tc": 6,
    "abs": 6,
    "engineMap": 7,
    "globalGreen": 1
  }
}
```

## Schema Reference

| Field | Type | Description |
|---|---|---|
| `packetId` | int | Frame counter |
| `status` | string | `"AC_OFF"`, `"AC_REPLAY"`, `"AC_LIVE"`, `"AC_PAUSE"` |
| `session` | string | `"AC_PRACTICE"`, `"AC_QUALIFY"`, `"AC_RACE"`, etc. |
| `currentTime` | string | Current lap time `"m:ss:mmm"` |
| `lastTime` | string | Last completed lap time |
| `bestTime` | string | Session best lap time |
| `split` | string | Current sector/split time |
| `iCurrentTime` | int | Current lap time in **milliseconds** |
| `iLastTime` | int | Last lap time in **milliseconds** |
| `iBestTime` | int | Best lap time in **milliseconds** |
| `iSplit` | int | Split time in milliseconds |
| `completedLaps` | int | Number of laps completed |
| `position` | int | Current race position |
| `sessionTimeLeft` | float | Time remaining in session (seconds, -1 if unlimited) |
| `distanceTraveled` | float | Distance since session start (meters) |
| `normalizedCarPosition` | float | 0.0–1.0 position on track spline |
| `isInPit` | int | Car in pit box |
| `isInPitLane` | int | Car in pit lane |
| `currentSectorIndex` | int | Current sector (0-indexed) |
| `lastSectorTime` | int | Last sector completion time ms |
| `numberOfLaps` | int | Total laps in session |
| `activeCars` | int | Number of cars on track |
| `carCoordinates` | Coords[60] | World XYZ positions of all 60 possible cars |
| `carIDs` | int[60] | IDs matching `carCoordinates` |
| `playerCarID` | int | ID of the player's own car |
| `tyreCompound` | string | Current tyre compound name |
| `penaltyTime` | float | Seconds of drive-through penalty |
| `penalty` | string | Active penalty type (enum string) |
| `flag` | string | Current race flag (`"AC_NO_FLAG"`, `"AC_YELLOW_FLAG"`, etc.) |
| `idealLineOn` | int | Ideal racing line display enabled |
| `surfaceGrip` | float | Track surface grip level |
| `mandatoryPitDone` | int | Mandatory pit stop served |
| `windSpeed` | float | Wind speed m/s |
| `windDirection` | float | Wind direction radians |
| `tc` | int | TC setting (1–12) |
| `tcCut` | int | TC Cut setting |
| `abs` | int | ABS setting (1–12) |
| `engineMap` | int | Engine map (1–7) |
| `fuelXLap` | float | Fuel used per lap |
| `usedFuel` | float | Fuel used this stint |
| `fuelEstimatedLaps` | float | Estimated laps remaining on current fuel |
| `rainLights` | int | Rain lights on |
| `flashingLights` | int | Flashing lights on |
| `lightsStage` | int | Headlight stage |
| `exhaustTemperature` | float | Exhaust temp °C |
| `wiperLV` | int | Wiper setting level |
| `driverStintTotalTimeLeft` | int | Total stint time left (ms, -1000 if disabled) |
| `driverStintTimeLeft` | int | Current stint time left (ms) |
| `rainTyres` | int | Rain tyres fitted |
| `sessionIndex` | int | Session number index |
| `deltaLapTime` | string | Delta vs best `"+/-m:ss:mmm"` |
| `iDeltaLapTime` | int | Delta in milliseconds |
| `isDeltaPositive` | int | 1=slower than best, 0=faster |
| `estimatedLapTime` | string | Predicted lap time |
| `iEstimatedLapTime` | int | Predicted lap time ms |
| `isValidLap` | int | Current lap is valid (no cuts) |
| `trackStatus` | string | `"FAST"`, `"SLOW"`, `"DAMP"`, etc. |
| `missingMandatoryPits` | int | Mandatory pits still required |
| `clock` | float | Real-world race clock (seconds from midnight) |
| `globalYellow` | int | Yellow flag sector 0 |
| `globalYellow1` | int | Yellow flag sector 1 |
| `globalYellow2` | int | Yellow flag sector 2 |
| `globalYellow3` | int | Yellow flag sector 3 |
| `globalGreen` | int | Green flag active |
| `globalWhite` | int | White flag (last lap) |
| `globalChequered` | int | Chequered flag |
| `globalRed` | int | Red flag |
| `directionLightsLeft` | int | Left indicator |
| `directionLightsRight` | int | Right indicator |
| `mfdTyreSet` | int | MFD selected tyre set |
| `mfdFuelToAdd` | float | MFD fuel to add (litres) |
| `mfdTyrePressure` | float[4] | MFD target tyre pressures [FL, FR, RL, RR] |
| `trackGripStatus` | string | `"Green"`, `"Fast"`, `"Optimum"`, `"Greasy"`, `"Damp"`, `"Wet"`, `"Flooded"` |
| `rainIntensity` | string | `"NoRain"`, `"Drizzle"`, `"LightRain"`, `"MediumRain"`, `"HeavyRain"`, `"Thunderstorm"` |
| `rainIntensityIn10min` | string | Predicted rain in 10 min |
| `rainIntensityIn30min` | string | Predicted rain in 30 min |
| `currentTyreSet` | int | Currently active tyre set number |
| `strategyTyreSet` | int | Strategy-planned next tyre set |
| `gapAhead` | int | Gap to car ahead (ms) |
| `gapBehind` | int | Gap to car behind (ms) |
| `isSetupMenuVisible` | int | Setup screen open |
| `mainDisplayIndex` | int | Primary display page |
| `secondaryDisplayIndex` | int | Secondary display page |
