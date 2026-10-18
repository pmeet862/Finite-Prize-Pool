# Docs (submission deliverables)

This folder holds the documents handed in with the assessment (brief §7):

| File | Brief item | Status |
|---|---|---|
| `DESIGN_NOTE.md` | §7.6 design note answering §8.2 | Not written yet (Phase 9) |
| `deal-size-table.csv` | §7.2 deal-size table, all 40 mode/bet combinations | Not generated yet (Phase 7) |
| `SIMULATION_REPORT.md` | §7.4 simulation report (10 × Inner $1.00, 1 × Outer $1.00) | Not generated yet (Phase 7) |

Anything generated before the *Finite Pool Mathematics* PDF arrives uses **placeholder** bag values. Such files say so in their header.

## Starting over while testing

The game saves everything to one folder: `Application.persistentDataPath/FinitePool`. On Windows that is `%USERPROFILE%\AppData\LocalLow\DefaultCompany\Finite Prize Pool\FinitePool`. The folder holds:

- every deal file,
- `state.json` (balances, deal positions, unshown results, completed and retired records),
- the spin log.

To wipe it and start fresh, use the Unity menu bar: **Finite Pool → Delete Saved Data**. It works like Unity's *Edit → Clear All PlayerPrefs*:

- It asks for confirmation, then deletes the folder.
- The next Play starts from scratch: both test players get their starting balance, and fresh deals are built.
- It is greyed out in Play Mode, because a deal still building in the background could be writing to the folder.
