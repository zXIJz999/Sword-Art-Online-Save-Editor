# Sword Art Online: Fatal Bullet Save Editor

**Made by zXIJz**

A free save editor for **decrypted PS4 saves** of Sword Art Online: Fatal Bullet.

> **This editor is free.** If you paid for it, or paid someone for an edited save, you were
> ripped off.

> Test on a copy of your save first.

## What it edits

- **Characters** (player and ArFA-sys of every save slot): EXP (level), credits, CP, SP, stat points.
- **Looks and costumes**: name, gender, face paint, hair, face and eyebrows, plus weapons,
  costume and accessories for every character. Every costume, accessory and weapon in the game
  can be picked, even ones you do not own.
- **Avatar**: replace a character's whole avatar with another character's, a saved avatar of the
  slot, a character from another save file, or a `.saoavatar` file (save and share them).
- **Skills**: pick the 4 equipped skills (and gadgets) for every weapon type.
- **Inventory**: add any item (search, filter by kind or DLC), quantities, weapon levels, and
  add, change or remove weapon chips. Added guns get the right weapon type so the game can equip them.
- **Progress and options**: play time, reward points, medals, ArFA-sys pocket money and AI
  settings, companion affinity, and every game option (volumes, camera speeds, displays,
  difficulty...).
- **Advanced (everything)**: written in plain English with a note for every row. Every value the save holds, in the game's own order: quests, story
  progress flags, party, shops, map records, appearance, names, equipment, the item box, player
  cards and more. Edit any value, rename, add or remove list entries. Edits that would break the
  game's layout are refused. Checked against one save so far: report any save it cannot read.

## Warning

Fatal Bullet has online co-op and PvP. You use edited saves at your own risk. The editor only
changes your own save file. It does not decrypt or encrypt PS4 saves: use your own save tools.

## How to use it

1. Export your save from the PS4 and **decrypt it**. The file is `ue4savegame.ps4.sav`.
2. Run `FatalBulletSaveEditor.exe`, click **Open a file...** and pick it.
3. Edit what you want on the Characters, Looks, Avatar, Skills, Inventory, Progress and Advanced pages.
4. Click **Save**. The editor **signs** the save so the game accepts it, and keeps your old
   save next to it as `<name>.<date-time>.bak`.
5. **Re-encrypt** the save and import it back to the PS4.

Paid DLC items are tagged **DLC** and can be filtered.

## Credits

| Who | For |
|---|---|
| **zXIJz** | Creating the editor and mapping the save format |
| [alfizari](https://github.com/alfizari/Sword-Art-Online-FB-Inventory-Editor) | Save signature key, item and chip name lists (Apache-2.0) |
| [DevExpress WinForms](https://www.devexpress.com/products/net/controls/winforms/) | The user interface controls |
| [.NET 8](https://dotnet.microsoft.com/) | The runtime |
| Dimps, Bandai Namco | The game |

This is a fan-made tool. It is not made, approved or supported by Bandai Namco or Dimps.

## Use it in your browser (no download)

Open the **web editor** on this repository's GitHub Pages site: `https://<owner>.github.io/<repo>/`
(the link is in the repository's About box). It does what the exe does and works on any computer
with a modern browser. It runs entirely on your own PC: your save is **never uploaded**.
**Save (download)** gives you the edited file with the same name; your original file is not
changed, so it is your backup. PS4 saves must still be decrypted first, and PC saves open too.

| Page | What you can change |
|---|---|
| **Characters** | EXP (level), credits, CP, SP and stat points of every character |
| **Looks and costumes** | Name, gender, face paint, hair, face, eyebrows, weapons, costume and accessories |
| **Avatar** | Copy a whole avatar from another character, a saved avatar, or an avatar file |
| **Skills** | The 4 equipped skills for every weapon type |
| **Inventory** | Add any item, quantities, weapon levels, chips |
| **Progress** | Play time, reward points, medals, ArFA-sys money and AI, affinity, game options |
| **Advanced** | Every value in the save, in plain English, with a note for each row |
| **Log** and **About** | What was changed, credits and links |

## For developers

```
dotnet build SaoSave.sln -c Release
dotnet publish src\SaoSave.Editor -c Release -r win-x64 -o publish
src\SaoSave.Tests\bin\Release\net8.0\SaoSave.Tests.exe <ue4savegame.ps4.sav>
```


`SaoSave.Web` is the browser version (Blazor WebAssembly, no DevExpress needed). Every write goes
through `SaoSave.Core`, so the exe and the web page write the same bytes. Run it locally with
`dotnet run --project src\SaoSave.Web`. To publish it, set **Settings > Pages > Source: GitHub
Actions** in the repository; every push to `main` then updates the site
(`.github/workflows/pages.yml`).

Needs the .NET SDK 8+. The desktop editor also needs DevExpress WinForms 25.2 (paid); `SaoSave.Core` and `SaoSave.Web` do not.
Save format notes: [docs/RESEARCH.md](docs/RESEARCH.md). `items.tsv` / `chips.tsv` are built
from alfizari's `items.json` / `chips.json` (Apache-2.0); the DLC flags come from the game's own
`ContentDefinition` asset.
