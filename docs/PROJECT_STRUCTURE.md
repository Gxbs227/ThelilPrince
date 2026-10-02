# Project structure and teamwork

Three programmers, one Unity project, one scene per stage.

## The repository

```
ThelilPrince/                       (the Git repository)
├── Unity/                          ← THE Unity project: open this folder in Unity Hub
│   ├── Assets/
│   │   ├── LittlePrince/           ← everything we make
│   │   └── ThirdParty/             ← Asset Store / downloaded packs, untouched
│   ├── Packages/                   ← (commit it) package list
│   ├── ProjectSettings/            ← (commit it) project settings
│   └── Library/, Temp/, Logs/      ← generated, ignored by Git
├── HandTracking-Python/            ← MediaPipe; the PythonLauncher finds it automatically
└── docs/                           ← flowchart, this file
```

## Inside `Assets/LittlePrince`

```
LittlePrince/
├── Scenes/
│   ├── Main.unity                  shared: Hand Tracking, Player (camera), Story Flow, UI
│   ├── Stages/
│   │   ├── 01_Desert.unity         black space, stars, footprints, gate
│   │   ├── 02_Garden.unity         roses, cliff
│   │   ├── 03_Village.unity        children, station, switchman, fox, origami
│   │   ├── 04_Space.unity          planets, stars, the rose
│   │   └── 05_Final.unity          third-person view, sunset, quote
│   └── Sandbox/                    personal test scenes (e.g. HandTrackingTest), not in the build
│
├── Stages/                         content that belongs to ONE stage
│   ├── 01_Desert/  Models/ Materials/ Textures/ Prefabs/ Animations/ Audio/ Lighting/ Scripts/
│   ├── 02_Garden/  (same folders)
│   ├── 03_Village/
│   ├── 04_Space/
│   └── 05_Final/
│
├── Characters/                     appear in several stages
│   ├── LittlePrince/  Models/ Materials/ Textures/ Animations/ Prefabs/
│   ├── Rose/  Fox/  Children/  Switchman/  Aviator/
│
├── Shared/                         used by more than one stage
│   ├── Models/ Materials/ Textures/ Shaders/ Prefabs/ VFX/ Skyboxes/
│
├── Audio/        Music/ Ambience/ SFX/            (shared sounds; stage-only sounds go in Stages/0X/Audio)
├── UI/           Fonts/ Sprites/ Prefabs/         (hints, quote, title)
├── Cinematics/   Timelines/ Video/                (final animation, planet fly-through)
├── Settings/     Rendering/ PostProcessing/ Lighting/   (URP assets, volume profiles)
│
└── Scripts/                        shared code
    ├── HandTracking/               MediaPipe receiver, cursor, gestures, Python launcher
    ├── GuidedWalk/                 path, stops, walker
    └── Story/                      story flow (flowchart), stage scenes menu
```

**Rule of thumb:** used in only one stage → `Stages/0X_Name/…`. Used in two or more → `Shared/` or `Characters/`.
Stage-only scripts go in `Stages/0X_Name/Scripts/`; code everyone uses goes in `Scripts/`.

**Naming:** `PascalCase`, no spaces or accents (`RoseGlass.fbx`, `M_Sand.mat`, `T_Sand_Albedo.png`).
Prefixes help searching: `M_` material, `T_` texture, `P_` prefab (optional, but be consistent).

## Who owns what (avoids merge conflicts)

Unity scenes and prefabs **cannot be merged** well by Git. If two people edit the same scene at the
same time, one person's work is lost. So:

| Person | Owns (only they edit it) |
|---|---|
| Programmer 1 | `01_Desert.unity` + `Stages/01_Desert/`, and `05_Final.unity` + `Stages/05_Final/` |
| Programmer 2 | `02_Garden.unity` + `Stages/02_Garden/`, and `04_Space.unity` + `Stages/04_Space/` |
| Programmer 3 | `03_Village.unity` + `Stages/03_Village/` (the longest stage) |
| Agree first | `Main.unity`, `Shared/`, `Characters/`, `Scripts/`: say in the group chat before editing |

(Swap the names as you like; the important part is **one owner per scene**.)

- Want to try something in someone else's stage? Make a copy in `Scenes/Sandbox/YourName_Test.unity`.
- Build reusable things as **prefabs** (a rose bed, a lamp): editing a prefab doesn't touch the scene file.
- Commit and push often (small commits), and **pull before you start** working.

## One-time setup (the first person)

1. In Unity: **Edit → Project Settings → Editor**:
   - **Version Control → Mode: Visible Meta Files**
   - **Asset Serialization → Mode: Force Text**
2. Copy your existing project's `Packages/` and `ProjectSettings/` folders into the repo's `Unity/` folder
   (next to `Assets/`), plus any assets you already made into the right folders above.
3. Open `Unity/` in Unity Hub (Add → select the `Unity` folder). Use the **same Unity version** as everyone.
4. Menu **Little Prince → Create Stage Scenes**: creates `Main` + the 5 stage scenes and fills in Build Settings.
5. Commit **everything including the `.meta` files** and push.

## Everyone else

1. Install **GitHub Desktop** (easiest; it also handles Git LFS for big files) and clone the repository.
2. In Unity Hub: **Add → the `Unity` folder** of the clone, same Unity version.
3. Python once: in `HandTracking-Python`, `python -m venv .venv` and
   `.venv\Scripts\python.exe -m pip install -r requirements.txt`.

Daily: **Pull → work in your own scene → Commit → Push.**

## About `.meta` files

Every file and folder in `Assets/` has a `.meta` file with its ID. Scenes and prefabs reference
scripts, models and materials **by that ID**. Always commit `.meta` files together with their asset,
and never delete/replace folders in Explorer (the IDs change and scenes show "Missing script").
Move and rename things **inside Unity's Project window**, which keeps the IDs.

## Big files

GitHub rejects files over 100 MB. `.psd`, `.blend`, `.tif`, `.exr`, `.wav`, `.mp4` and `.mov` go
through **Git LFS** automatically (see `.gitattributes`). The free LFS quota is 1 GB, so prefer
exporting `.png` / `.fbx` / `.ogg` into Unity and keep huge source files outside the repo.
