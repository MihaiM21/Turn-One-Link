# 🏎️ Turn One Link

**Turn One Link** is a high-performance Windows bridge application designed to stream real-time telemetry data from professional sim racing titles directly to the **Turn One** mobile ecosystem. 

It acts as the translator between complex simulation engines (iRacing, ACC, Assetto Corsa) and your mobile dashboard, ensuring zero-lag performance for competitive racing.

---

## 📍 Roadmap

The development of Turn One Link is divided into five strategic phases:

### Phase 1: Core Engine 🧠 (In Progress)
* [x] **Modern UI:** Minimalist, borderless Windows interface.
* [ ] **WebSocket Server:** Low-latency local broadcasting on port 8080.
* [ ] **T1 Protocol:** Unified JSON schema for all supported games.
* [ ] **Connection Heartbeat:** Auto-recovery system for lost mobile signals.

### Phase 2: First Contact (Assetto Corsa Support) 🏁
* [ ] **Shared Memory Integration:** Direct RAM reading for Assetto Corsa & ACC.
* [ ] **Auto-Hook:** Intelligent game process detection (Launch & Stream).
* [ ] **Basic Telemetry:** Speed, Gear, RPM, and Pedal Inputs.

### Phase 3: Pro Metrics 📊
* [ ] **Tire Physics:** Monitoring core/surface temps and pressure (PSI).
* [ ] **Delta Engine:** Real-time +/- comparison against your session best.
* [ ] **Fuel Management:** Live fuel-per-lap consumption and stint estimation.

### Phase 4: Elite Titles & Persistence 🏆
* [ ] **iRacing SDK:** Implementation of the official iRacing telemetry wrapper.
* [ ] **Session Logging:** Local storage of telemetry for post-race analysis.
* [ ] **Multi-Device Sync:** Stream to multiple tablets/phones simultaneously.

### Phase 5: Public Release 🚀
* [ ] **Auto-Updater:** Seamless background updates for new game patches.
* [ ] **Lightweight Installer:** Easy setup with all dependencies included.
* [ ] **Tray Integration:** Full "Minimize to Tray" functionality.

---

## 🛠️ Tech Stack
* **Language:** C# / .NET 8
* **UI Framework:** WPF (Windows Presentation Foundation)
* **Networking:** WebSockets (WatsonWebsocket)
* **Data Format:** JSON

## 🤝 Contributing
Want to help build the future of sim racing telemetry? Feel free to fork the repo and submit a Pull Request, especially if you have experience with iRacing SDKs or Memory Mapping.

---
*Developed by Turn One. Driven by data. Optimized for the win.*