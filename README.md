# LostArkFarmer

A lightweight, background-capable automation utility for **Lost Ark**.
This tool utilizes **ViGEm** (Virtual Gamepad Emulation) to simulate Xbox 360 controller inputs, allowing scripts to execute safely while the game runs in the background.

![Platform](https://img.shields.io/badge/platform-Windows-0078D6.svg)
![License](https://img.shields.io/badge/license-MIT-green.svg)
![Status](https://img.shields.io/badge/status-Active-brightgreen.svg)

## ✨ Features

* **Background Execution**: Works even when the game window is not focused (requires Borderless/Windowed mode).
* **Virtual Controller Emulation**: Uses `ViGEmBus` to mimic physical Xbox 360 hardware signals, bypassing standard software input detection.
* **JSON-Based Profiles**: Fully customizable rotation logic via `script_profile.json`.
* **Auto-Updater**: Automatically checks for the latest releases from GitHub and updates seamlessly.
* **Smart Input Handling**: Supports both **Sequential** (Combo inputs) and **Simultaneous** (Directional movement) input modes.

## 🛠️ Prerequisites

Before running the application, ensure you have the following installed:

1.  **ViGEmBus Driver** (Required for virtual controller creation)
    * Download and install the latest `ViGEmBus` installer (x64).
2.  **.NET Runtime** (If not already installed via Windows Update).

> **Game Settings Note**: You must set Lost Ark to **"Borderless"** or **"Windowed"** mode. Fullscreen mode pauses rendering when alt-tabbed, which will stop the automation.

## 🚀 Usage

1.  Download the latest release from the [Releases Page](../../releases).
2.  Right-click `LostArkFarmer.exe` and select **Run as Administrator**.
    * *Admin rights are strictly required to create virtual hardware devices.*
3.  The console will launch and connect to the virtual controller.
4.  **Hotkeys**:

| Key | Function | Description |
| :--- | :--- | :--- |
| **F9** | **Start** | Begins the script rotation defined in the config. |
| **F10** | **Stop** | Immediately pauses execution and resets controller inputs. |
| **Ctrl+C** | **Exit** | Closes the application completely. |

## ⚙️ Configuration (`script_profile.json`)

The logic is defined in `script_profile.json`. The application will generate a default profile if one does not exist.

### Parameters

* `LoopDelayMs`: Time to wait (in ms) after finishing the entire list of skills before starting over.
* `Skills`: An array of steps to execute.

### Skill Step Structure

| Property | Type | Description |
| :--- | :--- | :--- |
| `Note` | `string` | Label for the console log (e.g., "Skill Q", "Move Left"). |
| `Buttons` | `array` | List of buttons to press (see Supported Buttons below). |
| `PressDurationMs` | `int` | How long to hold the buttons down (ms). |
| `CoolDownMs` | `int` | Delay after releasing the buttons before the next step. |
| `IsSequential` | `bool` | `true` for combos (press A, wait, press B). <br>`false` for simultaneous press (e.g., Movement: LEFT + UP). |

### Supported Buttons

`A`, `B`, `X`, `Y`, `LB`, `RB`, `LT`, `RT`, `Start`, `Back`, `UP`, `DOWN`, `LEFT`, `RIGHT`

### Example Profile

```json
{
  "LoopDelayMs": 0,
  "Skills": [
    {
      "Note": "Skill Q (Combo)",
      "Buttons": [ "LB", "X" ],
      "PressDurationMs": 100,
      "CoolDownMs": 100,
      "IsSequential": true
    },
    {
      "Note": "Move Top-Left",
      "Buttons": [ "LEFT", "UP" ],
      "PressDurationMs": 750,
      "CoolDownMs": 150,
      "IsSequential": false
    }
  ]
}
```


https://github.com/user-attachments/assets/f5cf7e90-a12a-42ee-84f4-e148b455eda5


⚠️ Disclaimer

This software is for educational purposes only. Using automation tools in online games may violate the Terms of Service. The developer is not responsible for any bans or penalties resulting from the use of this tool. Use at your own risk.
