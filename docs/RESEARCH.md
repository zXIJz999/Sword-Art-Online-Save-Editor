# SAO Fatal Bullet save: research notes

## File

PS4: `ue4savegame.ps4.sav` (decrypted with your own save tools). **Not** UE4 GVAS: the game
writes its own binary layout (no field names). Starts with `01 00 00 00`.

## Signature (confirmed)

The file ends with `14 00 00 00` + a 20-byte HMAC-SHA1 of every byte before that marker, with
key `1FB00CC8D8D94CD0A94C847C2F04A921` (ASCII). Recomputed on a real save: matches. Every edit
must re-sign the file. (PC saves are additionally AES-ECB encrypted.)

Source: alfizari's Apache-2.0 tools
([inventory editor](https://github.com/alfizari/Sword-Art-Online-FB-Inventory-Editor),
[save transfer](https://github.com/alfizari/Sword-Art-Online-Fatal-bullet-save-transfer)).

## Strings

FString: int32 length (including the terminating zero), ASCII bytes, `00`. Empty item slots
hold `None`. Player name near the start (offset 0x35 in the test save).

## Item record (confirmed by parsing every array cleanly)

| Part | Size |
|---|---|
| id (FString, e.g. `WHG51_0000`, `ACC600_000_C`, `COS036_010_F`, `BUL00_00`) | 4 + n |
| fixed head: quantity (int32 at +0), then fields not yet named | 35 |
| chip count | 4 |
| chips: id (1 byte) + effect (float, 0.01 = 1%) | 5 each |
| fixed tail | 32 |

So a record with no chips is id + 71 bytes; each chip adds 5.

## Arrays found (test save)

An array is an int32 count followed by that many records.

| Offset | Slots | What it looks like |
|---|---|---|
| 0x1106 | 800 | all empty (storage?) |
| 0x4BB31 | 500 | the player's inventory (weapons, bullets, materials, costumes) |
| 0x6D540, 0xB1A10, 0xF5159, 0x1388A2 | 500 | more inventories (other save blocks / slots?) |
| 0x95FA2, 0xD96EB, 0x11CE34 | 200 | 200-slot inventories |
| ~380 arrays | 25 | equipment sets: 1 costume, 2 accessories, up to 2 weapons each (companions / presets) |

Names: `items.json` (alfizari, 1,488 IDs) and the game's own `Game.locres` (`ITM_*` keys).

## To do

- Name the 35-byte head and 32-byte tail fields (rarity, level, grade, lock/new flags...).
- Find money, level, stats and skills outside the item arrays.
- Work out why the item arrays repeat (backup slots?) and which one the game reads.

## Reversed from the game (eboot, IDA) - 2026-11-12

The game's save classes are reflected UE structs (`VGSave*`), but each has a hand-written
serializer (the module around eboot `0x94xxxx`), so the byte order is the serializer's, not the
declaration order. Found by matching each struct's field offsets against every function.

- `VGSaveCharacterDataBlock` (loader `0x94E020`): name, gender u8, experience, money,
  customize_point (CP), skill_point (SP), assigned_skill_point, skills TArray<i32>,
  `VGStatusCustomizePointAssignment` {strength, agility, vitality, dexterity, intelligence, luck},
  equip_skill (10 weapon layers x 4 skill ids), favorite_skill_set, avatar_customize, figure...
- Level is not stored: it comes from total experience.
- The file holds 4 save slots (`VGSaveSlotChunk`), each with a player and an ArFA-sys block and
  its own inventories; unused slots hold defaults ("Player", "Afasys", 10000 credits).
- `VGSaveInventoryItemEntry`: item_id, possession_amount, flag, category (EVGItemCategory:
  Weapon 0, Bullet 1, Costume 2, Accessory 3, Gadget 4, Material 5, KeyItem 6, None 7),
  entry_id, weapon_param {division (weapon type), classify, variation, m_attack, equip_option,
  ..., m_passive_skills (chips), m_customize_skill_num}, accessory_param {passive_skills},
  material_override.
- Field names of every save chunk (options, ArFA-sys money/AI, play record, quests, party,
  reputation, shop, map...) are listed by the eboot; the work files are in `work\ida`.

## Mapped for editing (2026-11-12)

Ported from the serializers in the eboot module; every part checked against the test save.

- **Save root** (`0x941560`): common chunk, then int32 slot count (4), then each slot.
- **Game options** (`0x9432F0`): 9 floats (BGM, SE, voice volume; camera, aim and scope speeds X/Y),
  3 bytes (camera horizontal, vertical, controller type), 8 bools as int32 (vibration, status
  displays, gun sight, mini map), mini map range u8, 3 bools (prize, damage, log window),
  brightness float, message speed float, demo subtitles bool, difficulty u8, 6 bools (send/receive
  player card, ArFA-sys join, voice chat, aim assist, increase reputation), then the key map.
- **Slot** (`0x947520`): play record, player, ArFA-sys, [abyss characters], equipment,
  inventories, progress, party (`0x9491D0`: char-id byte lists), reputation `TArray<i32>` resized
  to 50 (index = UCR character number, names from `CMN_Char_D_UCRnnn`), quests (`0x949770`),
  ArFA-sys data (`0x94B120`), shop, map records.
- **Play record** (`0x947D10`): play_time (float seconds), medals `TArray<i32>` (attack, support,
  weapon), reward_point, reward_received_point, status_customize_slot_index, activity_counts
  (15 ints), then version-gated values and customize presets.
- **ArFA-sys data**: talk counts, 4 shopping bytes, pocket_money int64, grand total int64,
  bank_money, bank rate, operation u8, quest clear count, emotion_action_ids `TArray<i32>`,
  emotion_set_id (FString, e.g. `BRIGHT`), AI type / battle style / recovery / rescue bytes.
- **Character equip_skill**: int32 10 layers, each `TArray<i32>` of 4 skill IDs (-1 empty).
  Layer order assumed to follow weapon division (0 handgun ... 7 photon sword).
- **Learned skills**: the character's `skills` `TArray<i32>` of skill IDs. IDs, names and SP cost
  come from the game's `VGSkillHeader` assets (244 skills, `skills.tsv`).

Not yet editable: quest records, progress flags, party lists, shop and map records (variable size,
nothing obvious to change without breaking story state).

## Full layout (2026-11-12)

`SaoTree` ports every reader the game uses and must consume the file to the last byte.

- **Header:** version (3 int32 + bool, 1.9.0), int32.
- **Common chunk (save path `0x941670`):**
  - int32
  - slot metadata (FText name, level, looks)
  - game options plus button assignment
  - item box
  - player card and received cards
  - 3 runs of N int32 lists, a 33-byte block, records of (int32, int32, bool, M x (bool, int32)), a byte block, bool.
  - N, M, record count and byte count come from the game's tables, not the file. They are solved per file: 50 lists, M = 2, 272 records, 4095 bytes on the test save.
- **Slot:** play record, player, ArFA-sys, abyss player and ArFA-sys, equipment, inventories and equipment sets (plus abyss), progress flags (bytes), party, affinity, quests, ArFA-sys data, int32, shops, current map, position, map records.
- **Item record:**
  - id, quantity, flag, category, entry id
  - weapon: division, level, variation, attack, option, 2 int32, chips (u8 + float)
  - customize skill count, customize skills (u8 + int32)
  - material overrides (u8, int32, name, name)
- **Learned skills:** `skills` is `TArray<uint8>` (count + bytes), not int32 IDs. What each byte means is not confirmed (needs a save with learned skills).
- **FText:** int32 flags, int8 history. Type 0 is followed by namespace, key and source strings. Type -1 is followed by nothing.
