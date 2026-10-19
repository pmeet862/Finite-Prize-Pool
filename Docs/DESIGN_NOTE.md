# Design note

This note answers the six questions in §8.2 and lists what was assumed or left out. The "Finite Pool Mathematics" PDF the brief refers to was not provided, so the bag contents are my own, chosen to reproduce every fact the brief states. The worked numbers below use them.

## The assumed bags

A bag is one cycle of tickets, the same at every bet. A multiplier ticket pays multiplier × bet as its whole award; a bonus ticket pays $1 per bonus unit. One Regular cycle plus one Big cycle is a paired cycle.

| Mode | Regular cycle | Big cycle | One paired cycle pays at $1.00 |
|---|---|---|---|
| Inner | ×1, ×1, ×2, ×2, ×3, bonus 5 | ×20, bonus 20 | 29 × $1 + 25 units = $54 |
| Outer | ×1 four times, ×2 three times, ×5, bonus 3 twice | ×50, ×25, bonus 40 | 90 × $1 + 46 units = $136 |

With these bags Inner $1.00 needs 1 paired cycle, Outer $1.00 needs 9, Outer $1.75 with one paired cycle would need 129.2 paid spins and is rejected, and Inner $1.00 with at most 5 consecutive losses is impossible (52 losses cannot fit into 9 runs of 5).

## Answers to §8.2

### 1. Why Inner fits in one paired cycle at $1.00 and Outer needs nine

A deal of k paired cycles pays k × C, where C is one paired cycle's payout. Exactly 90% needs N paid spins with N × bet × 0.9 = k × C, so N = 10kC ÷ (9 × bet). At $1.00 every award is whole dollars, so N = 10kD ÷ 9 for a payout of D dollars, and k × D must be divisible by 9. Inner pays D = 54, already a multiple of 9, so one paired cycle gives 60 spins (8 prizes, 52 losses). Outer pays D = 136, which has no factor of 3, so k must be 9, giving 1,360 spins.

### 2. Why Outer $4.75 needs such a large deal, and what that means without a timeout

In general k = 9b ÷ gcd(9b, 10C) with the bet b in cents. At $4.75, 9 × 475 = 3² × 5² × 19. The 5s cancel against 10C, but the 3² from 90% and the 19 from the bet can only be cancelled by C. Outer's C is 90 × 475 + 4,600 = 47,350 cents, divisible by neither 3 nor 19, so k = 171 and the deal has 18,940 spins, the largest in the table. The 19 would only cancel if the bonus units per paired cycle were a multiple of 19. Real bags would hold far more tickets per cycle, and N grows with C, so the real deal would be much larger.

A deal reaches 90% only at its last ticket, and nothing forces it to finish. Even 18,940 spins is about 16 hours of nonstop play at three seconds a spin. Until then, RTP over any shorter period can be far from 90% and big prizes can sit unclaimed. A config change does not reach that bet either, because an active deal keeps its config version, which is why the retire action exists. Tolerance mode, the §6 stretch goal, also built, shrinks it: with 90% ± 0.1 points allowed, Outer $4.75 needs 3 paired cycles and 332 spins at a planned 90.076%.

### 3. Why a ticket is never returned when the game closes mid-spin

The bet is charged, the ticket taken and the award credited in one saved commit before the animation starts. If closing the game returned the ticket, a player could quit whenever a spin looked like a loss and keep the wins, at the expense of everyone sharing the deal, and the deal's totals would no longer match its spin records or reach exactly 90%. On the next start the saved result is shown instead, and that player cannot spin again until it has been shown.

### 4. Does a player who plays 50 spins get 90% RTP?

No. 90% belongs to a whole deal, and every player on the device shares it. Fifty spins are a slice of one or two deals, and other players' spins take tickets from the same deal. In the 60-ticket Inner $1.00 deal, a player whose 50 spins hold all 8 prizes gets $54 on $50, which is 108%; one who misses the Big tickets gets much less. The placement limits stop the worst streaks (for Inner $1.00, at most 11 losses in a row and 40 slots between Big tickets) but do not make a short run pay 90%.

### 5. Why shuffling during building is acceptable when spins use no randomness

The shuffle only orders tickets whose total payout is already fixed, so every order pays exactly 90% when fully played, and the deal is verified and saved before anyone plays it. The randomness happens once, before any money is in play, using `RandomNumberGenerator.GetInt32` so the order cannot be predicted. A spin just reads the next ticket, so no result can be tuned per spin or per player, and every spin can be checked afterwards against the saved deal. A random draw at spin time would make RTP a probability again, which is what a finite pool avoids. A test confirms spins draw no random numbers.

### 6. What would change to move the engine to a server shared by many devices

The engine is plain C# behind `IDealService` with no Unity references, so the game would call a server implementing the same interface without changing its call sites. Storage would move from files to a database behind `IDealStore`, with each spin one transaction that locks the deal's cursor, charges the wallet, advances the cursor and writes the record; that replaces today's in-process lock and whole-file rewrite. Spin requests would carry an idempotency key so a network retry returns the original result instead of taking a second ticket. Balances would come from a wallet service with authenticated players, successors would be built by background workers, and deal files would be signed rather than only hashed. With many devices on one deal, its cursor becomes the contention point, so deals could be split per region.

## Decisions worth knowing

- Money is a `long` count of cents and RTP an exact fraction, so "exactly 90%" means payouts × 10 = wagers × 9. A deal that would need rounding is rejected with a reason. The calculator is checked against a 40-row table worked out separately in Python.
- The brief's shuffle-then-check procedure produced a valid layout in 0% of 200 attempts for every combination except Inner $1.00 (4.5%). The default generator, GapRepair, starts from the same shuffle, trims gaps that are too long and scatters the trimmed losses into gaps with room under all three limits. It succeeds on 87.5% to 100% of attempts and builds Outer $4.75 in about 3 ms, still passing the same validator and 1,000-attempt cap.
- Every deal is saved, read back and verified before use, from its saved tickets alone.

## Skipped or simplified

- The bags are assumptions, so questions 1 and 2 explain the general rule with these bags as examples.
- GapRepair layouts are random but not perfectly uniform over all valid layouts.
- The IMGUI game screen is plain, and it was tested by hand; the automated tests are Edit Mode tests of the engine.
- The simulator is a menu item for the required report, not a configurable window. The optional CSV audit export was not built; the Deal Admin window shows the records.
- Every spin rewrites the whole `state.json`, and startup reads the whole spin log. Fine at this scale, not on a server.
- A deal file's hash detects corruption, not deliberate tampering.

## Assumptions

- Bags hold prize tickets only; losses come from the category list. A multiplier × bet that is not whole cents makes that bet invalid rather than rounded. Deals over 50 million tickets are rejected.
- Fractional placement limits round down. A gap counts the slots between two separators, plus those before the first and after the last.
- A "fixed repeating interval" is prize spacing that repeats one pattern of length 1 to 4 across the whole deal. Limits that force one, such as a deal with no losses, are rejected before building.
- Cycles are shuffled once per build; the 1,000-attempt cap counts layout attempts.
- A mode and bet gets its first deal when first selected; after that a verified successor always waits.
- An unshown result belongs to the player who spun, who cannot spin again until it is shown.
- On a config change, a Ready successor on the old version is retired unused and rebuilt; a newer config at startup counts as a change. A failed build is recorded once and retried only on reselection or a config change.
- Tolerance mode tries 1 paired cycle first and picks the spin count closest to 90%, ties going to fewer spins. With these bags 8 of 40 combinations fit in one paired cycle.
