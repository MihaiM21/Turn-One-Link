# 12 — Ideas backlog

Unscheduled. An idea moves into [10-roadmap.md](10-roadmap.md) when it gets an owner decision.

| Idea | Why it might matter | Notes |
|---|---|---|
| **3D ghost replay** | Most shareable way to show "where I lose time" | three.js / react-three-fiber already in the FE; needs centerline + lateral offset |
| **Brake-point / racing-line heatmaps** per track | Shows where the field brakes; great track-page content | Aggregate of leaderboard laps' `LapCorners` |
| **Consistency score** | Beginners gain more from consistency than from pace | Std-dev per corner across a stint |
| **Driver rating (Elo-like)** | One number to chase across tracks | HF-32 |
| **Endurance stint & fuel planner** (ACC, LMU) | Leagues run 2–24 h races | Uses fuel-per-lap from `FuelManager` + tyre data |
| **Setup diff viewer** | "What did the fast guy change?" | Part of HF-10; could be standalone |
| **Pro reference laps** | VRS's main selling point | Partner with 2–3 fast drivers from the audience |
| **Seasonal rankings** aligned to the F1 season | Ties sim racing to the channel's calendar | Built on Weekly Hotlap points |
| **Auto-generated lap-comparison Shorts** | Free content for the channel each week | Remotion template, [09](09-mobile-and-ecosystem.md) |
| **"Drive the real lap"** | Compare your F1 25 Monza lap to the real pole lap's speed trace | T1API telemetry vs sim lap (different cars — a fun overlay, not a benchmark) |
| **Hardware profiles** | Wheel/pedals/rig in profile; filter leaderboards by "pad vs wheel" in F1 | Self-declared; useful for F1 console |
| **Rookie ladder** | Guided programme: 5 tracks, target times, tips | Built on coach + challenges |
| **Public API for leagues** | Leagues pull results into their sites | Through T1API's developer portal (`T1API_Console`) |
| **Replay import** | Import ACC/iRacing replays or IBT files after the fact | iRacing IBT is easy; ACC replays are not readable |
| **Wet/dry tyre and weather insights** | ACC/LMU rain is a big topic | Needs more channels from the adapters |
| **VR-friendly delta** | VR drivers can't see a phone | Would need an overlay (against D14 for now) |
