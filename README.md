# 🛡️ Keyboard Chatter Guard

Keyboard Chatter Guard is a premium, high-performance Windows utility designed to fix mechanical keyboard defects, specifically **keyboard chatter** (double-typing keys) and **ghost keys** (unwanted secondary characters firing alongside triggers). 

Built using **.NET 8.0**, **WPF**, and low-level **Win32 Input Hooking**, it provides an elegant, zero-latency solution to extend the life of your mechanical keyboard.

---

## ✨ Features

- **⚡ Invisible Ghost Key Suppression**: Uses a high-performance input-delay buffer to intercept and discard ghost keys *before* they ever reach the OS. No more flashing letters or glitchy backspace deletions!
- **👤 Per-Keyboard Profiles**: Save limits, chatter thresholds, and ghost rules isolated to specific hardware configurations.
- **🔄 Smart Preferred Device Auto-Switching**: 
  - Plugs in your favorite gaming keyboard? The app auto-switches to its specific profile.
  - Unplugs it? The app automatically falls back to your **Global Default Profile**.
- **🎨 Rich Cyberpunk Aesthetics**: A sleek glassmorphism dark theme interface with custom inline brand logo path badges (Razer, Logitech, Corsair, SteelSeries, HyperX) and a **flowing, animated neon outline** cycling around the active keyboard card.
- **📈 Real-Time Suppressed Statistics**: Track exactly how many double-taps and ghost keystrokes have been blocked on your desktop.
- **🗲 High-Fidelity Text Quality**: Configured with layout rounding and sub-pixel formatting to render crisp, pixel-perfect text on all high-DPI screens.
- **⚙️ Run on Startup & System Tray**: Minimizes cleanly to the Windows tray to run invisibly in the background.

---

## 🛠️ How It Works (Advanced Input Buffering)

Instead of using the old, glitchy method of letting ghost keys print on screen and then deleting them via a backspace stroke, Keyboard Chatter Guard uses a low-level Win32 `WH_KEYBOARD_LL` hook and thread-safe queue:

1. When a designated **Ghost Key** (e.g., `V` or `Y`) is pressed, the hook callback intercepts and consumes it, returning `1` to block it from reaching Windows.
2. A background delay timer (e.g., `40ms`) is scheduled.
3. If the **Trigger Key** (e.g., `B` or `T`) is pressed within this threshold, the ghost key is marked as `Discarded` and deleted from the queue.
4. If the threshold expires *without* the trigger key being pressed, the ghost key is cleanly injected back into the Windows input stream using `SendInput`.
5. *40 milliseconds is only 1/25th of a second—completely imperceptible to humans, but more than enough to capture hardware ghost keys which fire within 2-5ms.*

---

## 🚀 Quick Start & Installation

### Running the App
1. Download the pre-compiled standalone executable: **`KeyboardChatterGuard.exe`** (found under the Releases tab).
2. Double-click to run. (The app will launch in your system tray; double-click the tray icon to open the dashboard).

### Configuring Rules
1. Go to the **Keyboards** tab and select your keyboard to activate its profile.
2. Go to the **Dashboard** and configure your rules:
   - **For `B` and `V`**: Add a rule with **Trigger** = `B`, **Ghost** = `V`, and **Delay** = `40` (ms).
   - **For `T` and `Y`**: Add a rule with **Trigger** = `T`, **Ghost** = `Y`, and **Delay** = `40` (ms).
3. Try typing! The ghost letters will be filtered out instantly.

### Settings & Auto-Switching
1. Go to the **Settings** tab.
2. Under **Auto-Switch to Preferred Device**, select your keyboard from the dropdown menu.
3. Whenever that keyboard is connected, your profile and specific rule list will automatically load and activate.

---

## 💻 Developer Setup & Compiling

### Requirements
- .NET 8.0 SDK
- Windows OS (Required for Win32 API / Hooks)

### Build Command
Compile the single-file self-contained bundle with embedded application resources:
```bash
dotnet publish -c Release -r win-x64 --no-self-contained -p:PublishSingleFile=true -p:PublishReadyToRun=false
```
The output executable will be compiled to `bin/Release/net8.0-windows/win-x64/publish/KeyboardChatterGuard.exe`.
