# Finite Prize Pool

A Unity project for the Finite Prize Pool developer assessment. Instead of rolling a random result on every spin, the game builds each prize pool in advance as a deal: a fixed, shuffled list of Regular, Big and Loss tickets. Every spin takes the next ticket. When a deal has been played to its last ticket it has paid back exactly 90% of what was wagered.

The engine is plain C# in its own assembly, and the Unity game reaches it only through a small interface. Design decisions and the answers to the brief's questions are in [Docs/DESIGN_NOTE.md](Docs/DESIGN_NOTE.md).

## Requirements

- Unity 6000.0.66f2 (Unity 6 LTS).
- Packages restore on first open: the Unity Test Framework and Newtonsoft JSON (`com.unity.nuget.newtonsoft-json`).

## Open and run

1. In Unity Hub, choose Add, then Add project from disk, and select this folder.
2. Open `Assets/Scenes/SampleScene.unity`.
3. Press Play.

The game screen is on the left. Pick a player, a mode (Inner or Outer) and one of the 20 bet levels from $0.25 to $5.00, then press Spin. The result shows the win amount and the prize type: multiplier, bonus or loss. The first time a mode and bet are chosen, their deal is built in the background and the screen shows "Preparing deal…" for a moment.

The debug panel on the right shows the active deal's ID, state, config version and spins played. It also has a balance top-up and a "Fast spin ×10" button for reaching the end of a deal quickly. It is compiled only in the Editor and in development builds.

### Editor menu

Everything else is under the Finite Pool menu.

| Menu item | What it does |
|---|---|
| Deal Admin | In Play Mode: retire an active deal, apply a new config, and view every deal plus the completed and retired records. Outside Play Mode it shows the saved records read-only. |
| Export Deal-Size Table (CSV) | Writes the 40-row deal-size table to `Docs/deal-size-table.csv` and opens it. |
| Export Deal-Size Table, Tolerance Mode (CSV) | The same table with RTP allowed anywhere in 90% ± 0.1 points, written to `Docs/deal-size-table-tolerance.csv`. |
| Write Simulation Report | Plays 10 complete Inner $1.00 deals and 1 complete Outer $1.00 deal and writes `Docs/SIMULATION_REPORT.md`. |
| Delete Saved Data | Deletes all saved deals, balances and history so the next Play starts fresh. Disabled in Play Mode. |

The game saves to `Application.persistentDataPath/FinitePool`. On Windows that is `%USERPROFILE%\AppData\LocalLow\DefaultCompany\Finite Prize Pool\FinitePool`.

## Tests

Open Window > General > Test Runner, select EditMode and choose Run All. There are 208 Edit Mode tests. Two of them are marked `[Explicit]` and are skipped in a normal run: a statistical check of the shuffle, and a placement measurement that writes a report. The ten acceptance scenarios from the brief are in `ScenarioTests`, named `Scenario01` to `Scenario10`.

## How the code is organised

| Assembly | Folder under `Assets/FinitePool` | Contents |
|---|---|---|
| `FinitePool.Engine` | `Engine` | The deal engine. Plain C# with no UnityEngine reference (`noEngineReferences`), so it could run on a server unchanged. |
| `FinitePool.Game` | `Game` | `GameBootstrap` creates the engine and reads the save path; `GameClientView` is the game screen. |
| `FinitePool.Game.DebugTools` | `Game.DebugTools` | The debug panel. Compiled only when `UNITY_EDITOR \|\| DEVELOPMENT_BUILD`. |
| `FinitePool.Editor` | `Editor` | The admin window, the report menus and the delete-data menu. |
| `FinitePool.Tests.EditMode` | `Tests/EditMode` | NUnit tests. |

The game talks to the engine through three interfaces: `IDealService` (spin, unshown results, balances, preparing deals), `IDealAdmin` (retire, config changes, records) and `IDealDiagnostics` (the facts the debug panel may show). The deal and ticket types are internal to the engine, so code in the game, debug or editor assemblies cannot read the ticket list.

Inside the engine:

| Folder | Responsibility |
|---|---|
| `Core` | `Money` (whole cents in a `long`) and `Fraction` (exact RTP such as 9/10). |
| `Config` | Loads and validates the JSON config. |
| `Sizing` | The deal-size calculator and the CSV table. |
| `Building` | The Fisher-Yates shuffle (using `RandomNumberGenerator.GetInt32`), cycle streams, layout generation and the deal builder. |
| `Placement` | The placement limits, the up-front feasibility check and the validator. |
| `Verification` | Recomputes a saved deal from its tickets and confirms it before use. |
| `Persistence` | Crash-safe file writes, the compact deal file format and the saved state. |
| `Lifecycle` | Background building and the build, save, read back and verify sequence. |
| `Service` | `DealService`, the deal records and the simulator. |

A spin checks the player's balance first and is rejected without using a ticket if it is too low. Otherwise the engine charges the bet, takes the next ticket and credits the award on a copy of the state, and saves that copy to `state.json` with a temp-file-then-replace write. That save is the commit. The result goes back to the game only after it, so a game closed mid-animation shows the saved result on the next start and never uses a second ticket.

Each mode and bet keeps an active deal and a verified Ready successor, built on a background thread. A build sizes the deal, shuffles each Regular and Big cycle on its own, lays out the ticket categories within the placement limits, merges them, saves the deal, reads it back and verifies it. When the final ticket is used, the completed record and the switch to the successor are saved in the same commit.

## Configuration

`Assets/FinitePool/Config/deal-config.json` holds the bet levels, target RTP, bonus unit value, placement limits and bags, with a `configVersion`. The brief's math PDF was not provided, so the bag values are assumptions chosen to reproduce the facts the brief states; the design note lists them. Two more configs exist for demonstrations: `deal-config.tolerance.json` (tolerance mode) and `deal-config.v2-test.json` (a config change, applied from the Deal Admin window).

## Deliverables

| Item | Where |
|---|---|
| Design note | [Docs/DESIGN_NOTE.md](Docs/DESIGN_NOTE.md) |
| Deal-size table, all 40 combinations | [Docs/deal-size-table.csv](Docs/deal-size-table.csv) |
| Deal-size table in tolerance mode | [Docs/deal-size-table-tolerance.csv](Docs/deal-size-table-tolerance.csv) |
| Simulation report | [Docs/SIMULATION_REPORT.md](Docs/SIMULATION_REPORT.md) |
| Automated tests | `Assets/FinitePool/Tests/EditMode` |
| Screen recording | Provided separately |
