# Split-Screen Mod

<img width="910" height="512" alt="image2" src="https://github.com/user-attachments/assets/6c79721c-43c1-4f48-bd84-215023368ea3" />

## 📖 About
Adds support for 2-player split-screen to Batman: Arkham City, allowing you to play through the main story and challenge maps with multiple players.

Based on [BmSDK](https://bmsdk.dev), a C# modding framework for the game built by the same creators as the split-screen mod. Leave a star on both repos if you have fun with this!

## ⚠️ Warning

This is an early in-development version of the split-screen mod. Several things can break during the campaign, especially if P2 triggers certain actions instead of P1. If this happens, restart from checkpoint or use the ghost/teleport keybinds to get past it.

## 🚀 Getting started
Follow these steps to set up the mod for the first time:
1. Install [BmSDK](https://github.com/Team-BmSDK/BmSDK-AC#-getting-started)
2. Download the latest .zip from [here](https://github.com/etkramer/SplitScreenMod/archive/refs/heads/main.zip) and extract it into `%GameDir%\BmGame\Mods`
3. If you've done everything right, your `Mods` folder should now have a `SplitScreenMod-main` folder inside it
4. Done! Start the game as usual, then press "G" from the menu to add a new player

## 🎮 Controls
Like normal the first controller plugged in is used for P1 (auto-swaps with keyboard/mouse), so P2 needs a second controller.

- G: Add a player
- O: Remove a player
- T: Teleport all players to P1 (if you get stuck)
- Y: Teleport all players to P2 (if you get stuck)
- V: Toggle ghost (if you get stuck)

## 🛠️ Troubleshooting

If you run into issues getting the mod to work (i.e. the game crashes on startup, "G" doesn't work), try these steps:
1. Make sure you followed the install instructions exactly (for this and BmSDK)
2. If you're on Steam, double-check that you installed the BmSDK compatability patch
3. If you're on Steam and still nothing happens when you press a key, try running BatmanAC.exe directly instead of starting through Steam
4. Try using DX11 or DX9 mode - depending on what works better for you. You can change this by enabling or disabling DX11 features in the Arkham City Launcher. If the launcher doesn't work for you, you can try the [Advanced Launcher](https://www.nexusmods.com/batmanarkhamcity/mods/406) instead
