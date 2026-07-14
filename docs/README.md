# Turn One Link

**Turn One Link** is a high-performance Windows bridge application designed to stream real-time telemetry data from professional sim racing titles directly to the **Turn One** mobile ecosystem. 

It acts as the translator between complex simulation engines (iRacing, ACC, Assetto Corsa) and your mobile dashboard, ensuring zero-lag performance for competitive racing.

## 📚 Documentation Table of Contents

- [AI Agent Guide](AI_AGENT_GUIDE.md) - Start here if you are an AI assistant!
- [Architecture & Data Flow](architecture.md) - High-level system design and data flow.
- [Setup & Environment](setup/environment.md) - How to configure the environment.
- [Telemetry Overview](telemetry/overview.md) - General telemetry concepts and frontend implementation notes.
- [Physics Data](telemetry/physics.md) - Schema for real-time car physics.
- [Graphics Data](telemetry/graphics.md) - Schema for session and race state.
- [Static Data](telemetry/static.md) - Schema for session static info.
- [Backend Contract](telemetry/backend_contract.md) - Details on backend integration and session management.
- [T1 Protocol](telemetry/t1_protocol.md) - Unified local broadcast protocol for mobile clients.

---

## 📍 Roadmap

The development of Turn One Link is divided into five strategic phases:

### Phase 1: Core Engine ✅
* [x] **Modern UI:** Minimalist, borderless Windows interface.
* [x] **WebSocket Server:** Low-latency local broadcasting on port 8080 with PIN pairing.
* [x] **T1 Protocol:** Unified JSON schema for all supported games (see [T1 Protocol](telemetry/t1_protocol.md)).
* [x] **Connection Heartbeat:** Auto-recovery system for lost mobile signals (5s ping / 10s pong timeout).

### Phase 2: First Contact (Assetto Corsa Support) 
* [ ] **Shared Memory Integration:** Direct RAM reading for Assetto Corsa & ACC.
* [ ] **Auto-Hook:** Intelligent game process detection (Launch & Stream).
* [ ] **Basic Telemetry:** Speed, Gear, RPM, and Pedal Inputs.

### Phase 3: Pro Metrics
* [ ] **Tire Physics:** Monitoring core/surface temps and pressure (PSI).
* [ ] **Delta Engine:** Real-time +/- comparison against your session best.
* [ ] **Fuel Management:** Live fuel-per-lap consumption and stint estimation.

### Phase 4: Elite Titles & Persistence
* [ ] **iRacing SDK:** Implementation of the official iRacing telemetry wrapper.
* [ ] **Session Logging:** Local storage of telemetry for post-race analysis.
* [ ] **Multi-Device Sync:** Stream to multiple tablets/phones simultaneously.

### Phase 5: Public Release
* [ ] **Auto-Updater:** Seamless background updates for new game patches.
* [ ] **Lightweight Installer:** Easy setup with all dependencies included.
* [ ] **Tray Integration:** Full "Minimize to Tray" functionality.

### Phase 6: Cloud Analytics & AI Coaching
* [ ] **Corner Delta AI:** AI analysis of telemetry to identify exact corners for time improvement.
* [ ] **Web-Based Telemetry Replays:** Cloud uploads of high-frequency telemetry for interactive browser review.
* [ ] **Predictive Setup Suggestions:** Car setup changes suggested by tire temp and wear data.

### Phase 7: Mobile UI Excellence
* [ ] **Smart Adaptive Dashboards:** Mobile UI automatically switches layouts based on race context (e.g., pit-lane vs out-lap).
* [ ] **Glassmorphism & Micro-animations:** Premium UI design with smooth 60fps animations.
* [ ] **"One-Click" Setup:** Zero-configuration local network discovery for the mobile app.

---

## Monetization Strategy (Subscription Plans)

*   **Rookie Plan (Free):** Basic real-time telemetry streaming (Speed, RPM, Gears, Basic Inputs) with standard mobile dashboards over local network.
*   **Pro Plan ($4.99/mo):** Advanced physics (tire/brake temps, MGU-K), dynamic adaptive dashboards, fuel management engine, and limited cloud logging (last 10 sessions).
*   **Elite Plan ($9.99/mo):** AI-powered coaching (Corner Delta), unlimited cloud storage, global leaderboard trace comparisons, and predictive setups.

---

## Tech Stack
* **Language:** C# / .NET 9
* **UI Framework:** WPF (Windows Presentation Foundation)
* **Networking:** WebSockets (WatsonWebsocket)
* **Data Format:** JSON

## 🤝 Contributing
Want to help build the future of sim racing telemetry? Feel free to fork the repo and submit a Pull Request, especially if you have experience with iRacing SDKs or Memory Mapping.

---
*Developed by Turn One. Driven by data. Optimized for the win.*
