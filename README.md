# Floppy

Replaces the Ouroboros shop with Floppy, which exchanges a module for a random one of the same rarity.

## How to use

- Turn Floppy on or off with the switch on the side of the Ouroboros mission selection menu. You can also change it when continuing a run.
- Interact with Floppy and choose a temporary Ouroboros module from your inventory.
- Watch the tray close, listen to the disk being read, and collect a random different module of the same rarity.
- Each player gets three exchanges every time Floppy appears. After the third exchange, the computer shuts down.

## Multiplayer

All players must install the same version of Floppy. The lobby host controls whether Floppy appears for the whole team.

## Installation

### Thunderstore

Install Floppy with Thunderstore Mod Manager or r2modman. The required BepInEx pack is installed automatically. Launch Mycopunk through your mod manager.

### Manual

1. Install [BepInExPack Mycopunk](https://thunderstore.io/c/mycopunk/p/BepInEx/BepInExPack_Mycopunk/).
2. Extract the `BepInEx` folder from this package into your Mycopunk game folder.
3. Launch the game.

## Building from source

Requirements: Windows, Git, a .NET SDK with C# 10 or newer support, the .NET
Framework 4.8 Developer Pack, and a Mycopunk installation with BepInEx installed.

Run these commands in PowerShell:

```powershell
git clone https://github.com/Gelofrat/Floppy.git
cd Floppy
dotnet build Floppy.csproj -c Release -p:GameRoot="D:\SteamLibrary\steamapps\common\Mycopunk"
```

Replace the example `GameRoot` with your own Mycopunk game folder. It must
contain `Mycopunk_Data/Managed` and `BepInEx/core`; the required DLLs are
referenced from that installation.

The built plugin is `bin/Release/Floppy.dll`. Copy it into your game's
`BepInEx/plugins/Floppy/` folder. The model, textures and sounds are embedded
in the DLL.

## Contact

For questions or suggestions, feel free to contact me on [Discord](https://discord.com/users/970246624985120828).

## Author

Gelofrat

Created with AI assistance.

## License

MIT. See the included LICENSE file.
