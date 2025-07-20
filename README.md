# HeliosAI

> NPC AI Manager for Space Engineers

HeliosAI is a modular, Nexus-integrated AI system for autonomous NPC grid behavior in Space Engineers. Designed for performance and flexibility, it integrates WeaponCore combat support, dynamic behaviors, and future radar capabilities.

---

## ✅ Features

### 🧠 AI Architecture
- Modular AI behavior system (`Patrol`, `Attack`, `Retreat`, etc.)
- Per-grid `NpcEntity` logic
- Behavior switching with fallback/override support
- Centralized `Tick()` method per NPC

### ⚙️ Configuration & Logging
- Torch-compatible logging (info/warn/error)
- Config file support (radar toggle, default behaviors, etc.)
- Debug messages for transitions and decisions

### 🎯 WeaponCore Integration
- API registration and validation
- Auto weapon discovery per grid
- Weapon readiness and targeting
- Optional fallback to vanilla guns

### 👁 Radar & Targeting
- Auto-acquire nearby players/characters
- Priority targeting by proximity
- Visual radar (line draw from grid to target)
- Planned: actual radar scanning and threat scoring

### 🤖 Behavior Modules
- **PatrolBehavior** – idle movement/scanning
- **AttackBehavior** – target acquisition and pursuit
- **RetreatBehavior** – flee logic with direction logic

### 🔌 Optional / Planned Features
- ✅ Terminal UI for NPCs
- ✅ Radar toggling & debug
- ✅ Nexus zone support (per-zone behavior)
- ⏳ Faction hostility checks
- ⏳ Grid health-based retreat
- ⏳ Escort/follow behavior
- ⏳ Station defense mode
- ⏳ Remote control via command/chat

---

## 🔧 Requirements

- [TorchAPI](https://torchapi.net/) – Plugin framework
- [WeaponCore](https://steamcommunity.com/sharedfiles/filedetails/?id=1918681825) – Weapon integration
- [NexusAPI](https://github.com/torchapi/Nexus) *(optional)* – Zone logic

---

## 🚀 Getting Started

1. Place the HeliosAI plugin in your `Torch/Plugins` folder
2. Ensure WeaponCore is loaded as a mod
3. Start Torch and observe logs for NPC registration

---

## 📁 File Structure

- `NpcEntity.cs` – AI logic container for a grid
- `AiBehavior.cs` – Base class for behaviors
- `AttackBehavior.cs` – Aggressive logic and targeting
- `RetreatBehavior.cs` – Retreat on health/condition
- `WeaponCoreGridManager.cs` – WC weapon handling per grid
- `WeaponCoreAdvancedAPI.cs` – Safe access to WeaponCore delegates

---

## 📌 Status

HeliosAI is in **active development**. Expect rapid iterations, API hooks, and behavior improvements.

---

## 🙌 Contributing / Ideas?

Suggestions, bug reports, or pull requests welcome!

> Created with ❤️ by the Space Engineers AI community

