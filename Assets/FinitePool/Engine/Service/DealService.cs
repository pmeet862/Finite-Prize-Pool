using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FinitePool.Engine.Building;
using FinitePool.Engine.Config;
using FinitePool.Engine.Core;
using FinitePool.Engine.Deals;
using FinitePool.Engine.Infrastructure;
using FinitePool.Engine.Lifecycle;
using FinitePool.Engine.Persistence;
using FinitePool.Engine.Verification;

namespace FinitePool.Engine.Service
{
    /// <summary>
    /// The deal engine behind <see cref="IDealService"/>. Plain C#: no Unity types,
    /// so it could run on a server unchanged.
    ///
    /// Rules it keeps:
    /// - Every change is made on a copy of the state, saved atomically, and only then
    ///   made live (<see cref="Commit"/>). If the save fails, nothing changed.
    /// - A spin is saved before it is returned, so the client animates a result that
    ///   is already on disk. A used ticket is never handed out again.
    /// - Spins never use randomness. Randomness is only used to build deals.
    /// - Each mode/bet is kept stocked with an Active deal and a verified Ready
    ///   successor, built in the background (<see cref="TopUp"/>), so play never waits.
    /// - All changes happen under one lock, so a background build finishing at the
    ///   same moment as a spin can't interleave with it.
    /// </summary>
    public sealed class DealService : IDealService, IDealAdmin, IDealDiagnostics
    {
        private readonly object m_sync = new object();
        private readonly IDealStore m_store;
        private readonly IClock m_clock;
        private readonly IEngineLog m_log;
        private readonly IBackgroundScheduler m_scheduler;
        private readonly VerifiedDealFactory m_dealFactory;

        // Not saved: rebuilt from state and deal files at startup.
        private readonly Dictionary<string, Deal> m_loadedDeals = new Dictionary<string, Deal>();
        private readonly HashSet<string> m_combosBeingBuilt = new HashSet<string>();
        private readonly Dictionary<string, string> m_unavailableReasons = new Dictionary<string, string>();

        private DealConfig m_config;
        private EngineState m_state;

        internal DealService(DealConfig config, IDealStore store, IRandomSource random, IClock clock, IEngineLog log, IBackgroundScheduler scheduler)
        {
            m_config = config ?? throw new ArgumentNullException(nameof(config));
            m_store = store ?? throw new ArgumentNullException(nameof(store));
            m_clock = clock ?? throw new ArgumentNullException(nameof(clock));
            m_log = log ?? throw new ArgumentNullException(nameof(log));
            m_scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
            m_dealFactory = new VerifiedDealFactory(new DealBuilder(random, clock, log), store, log);

            m_state = m_store.ReadState() ?? EngineState.CreateNew(config.ConfigVersion);
            RepairSpinLog();

            if (m_state.ConfigVersion != config.ConfigVersion)
            {
                // A different config shipped since the last run counts as a config change (A-19).
                m_log.Info($"Config changed from {m_state.ConfigVersion} to {config.ConfigVersion} since the last run.");
                Commit(draft => SwitchConfig(draft, config.ConfigVersion, m_clock.UtcNow));
            }

            LoadDeals();
            foreach ((GameMode mode, BetLevel bet) in CombosWithActiveDeals())
            {
                TopUp(mode, bet, retryAfterFailure: false);
            }
        }

        /// <summary>The game's setup: files under <paramref name="rootFolder"/>, crypto shuffles, background builds.</summary>
        public static DealService OpenWithFiles(DealConfig config, string rootFolder, IEngineLog log) =>
            new DealService(config, new FileDealStore(rootFolder), new CryptoRandomSource(), new SystemClock(), log, new ThreadPoolScheduler());

        /// <summary>Editor tools (simulator): memory only, builds run immediately.</summary>
        public static DealService OpenInMemory(DealConfig config, IEngineLog log) =>
            new DealService(config, new InMemoryDealStore(), new CryptoRandomSource(), new SystemClock(), log, new ImmediateScheduler());

        public DealConfig Config
        {
            get { lock (m_sync) return m_config; }
        }

        public IReadOnlyList<BetLevel> BetLevels => Config.BetLevels;

        // ---------------------------------------------------------------- spins

        public SpinOutcome Spin(PlayerId player, GameMode mode, BetLevel bet)
        {
            SpinOutcome outcome;
            lock (m_sync)
            {
                outcome = SpinLocked(player, mode, bet);
            }

            if (outcome.IsAccepted)
            {
                // If that was the final ticket, the successor just became active and needs its own successor.
                TopUp(mode, bet, retryAfterFailure: false);
            }

            return outcome;
        }

        private SpinOutcome SpinLocked(PlayerId player, GameMode mode, BetLevel bet)
        {
            if (!m_config.HasBetLevel(bet))
            {
                return SpinOutcome.Rejected(SpinRejection.UnknownBetLevel, $"{bet} is not a bet level in config {m_config.ConfigVersion}.");
            }

            if (!m_state.Balances.TryGetValue(player.Value, out long balanceUnits))
            {
                return SpinOutcome.Rejected(SpinRejection.UnknownPlayer, $"Player '{player}' is not registered.");
            }

            if (m_state.PendingSpins.ContainsKey(player.Value))
            {
                return SpinOutcome.Rejected(SpinRejection.PreviousResultNotShown, $"Player '{player}' has a saved result that hasn't been shown yet.");
            }

            DealEntry entry = m_state.FindDeal(mode, bet, DealState.Active);
            if (entry == null || !m_loadedDeals.TryGetValue(entry.DealId, out Deal deal))
            {
                return SpinOutcome.Rejected(SpinRejection.NoActiveDeal, $"No playable deal for {mode} {bet}: {GetAvailabilityLocked(mode, bet)}.");
            }

            Money balance = Money.FromUnits(balanceUnits);
            Money wager = bet.Amount;
            if (balance < wager)
            {
                return SpinOutcome.Rejected(SpinRejection.InsufficientBalance, $"Balance {balance} is less than the bet {wager}. No ticket was used.");
            }

            SpinRecord record = CreateSpinRecord(player, entry, deal, balance, wager);
            try
            {
                Commit(draft => ApplySpin(draft, record));
            }
            catch (Exception exception) when (IsStorageFailure(exception))
            {
                m_log.Error($"Spin for {player} on {deal.DealId} could not be saved, so it did not happen: {exception.Message}");
                return SpinOutcome.Rejected(SpinRejection.StorageError, "The spin could not be saved. No ticket was used.");
            }

            if (m_state.FindDeal(deal.DealId).State == DealState.Completed)
            {
                m_loadedDeals.Remove(deal.DealId);
                m_log.Info($"Deal {deal.DealId} completed. Now active for {mode} {bet}: {m_state.FindDeal(mode, bet, DealState.Active)?.DealId ?? "none yet"}.");
            }

            AppendToSpinLog(record);
            return SpinOutcome.Accepted(ToResult(record));
        }

        public SpinResult GetPendingResult(PlayerId player)
        {
            lock (m_sync)
            {
                return m_state.PendingSpins.TryGetValue(player.Value, out SpinRecord record) ? ToResult(record) : null;
            }
        }

        public bool MarkShown(PlayerId player, long spinId)
        {
            lock (m_sync)
            {
                if (!m_state.PendingSpins.TryGetValue(player.Value, out SpinRecord record) || record.SpinId != spinId)
                {
                    return false;
                }

                Commit(draft => draft.PendingSpins.Remove(player.Value));
                return true;
            }
        }

        public Money GetBalance(PlayerId player)
        {
            lock (m_sync)
            {
                return m_state.Balances.TryGetValue(player.Value, out long units) ? Money.FromUnits(units) : Money.Zero;
            }
        }

        private SpinRecord CreateSpinRecord(PlayerId player, DealEntry entry, Deal deal, Money balance, Money wager)
        {
            int position = entry.Cursor;
            TicketType ticket = deal.GetTicket(position);
            Money balanceAfter = balance - wager + ticket.Award;

            return new SpinRecord
            {
                SpinId = m_state.NextSpinId,
                PlayerId = player.Value,
                Mode = deal.Mode,
                BetUnits = deal.Bet.Amount.Units,
                DealId = deal.DealId,
                TicketIndex = position,
                Category = ticket.Category,
                AwardKind = ticket.Kind,
                WagerUnits = wager.Units,
                AwardUnits = ticket.Award.Units,
                BalanceBeforeUnits = balance.Units,
                BalanceAfterUnits = balanceAfter.Units,
                CursorAfter = position + 1,
                DealWagersAfterUnits = checked(entry.WagersUnits + wager.Units),
                DealPayoutsAfterUnits = checked(entry.PayoutsUnits + ticket.Award.Units),
                TimestampUtc = m_clock.UtcNow
            };
        }

        /// <summary>
        /// Applies a spin using its absolute "after" values, so applying it twice changes
        /// nothing more (useful if a crash ever forces a replay).
        /// </summary>
        private static void ApplySpin(EngineState draft, SpinRecord record)
        {
            DealEntry entry = draft.FindDeal(record.DealId);
            draft.Balances[record.PlayerId] = record.BalanceAfterUnits;
            entry.Cursor = record.CursorAfter;
            entry.WagersUnits = record.DealWagersAfterUnits;
            entry.PayoutsUnits = record.DealPayoutsAfterUnits;
            draft.NextSpinId = Math.Max(draft.NextSpinId, record.SpinId + 1);
            draft.PendingSpins[record.PlayerId] = record;
            draft.LastCommittedSpin = record;

            if (entry.Cursor == entry.TicketCount)
            {
                CompleteDeal(draft, entry, record.TimestampUtc);
            }
        }

        /// <summary>
        /// The final ticket was used: record the outcome and hand over to the Ready successor,
        /// all in the same commit as the spin itself (brief §4.6).
        /// </summary>
        private static void CompleteDeal(EngineState draft, DealEntry entry, DateTime now)
        {
            entry.State = DealState.Completed;
            entry.ClosedUtc = now;
            draft.CompletedDeals.Add(new CompletedDealRecord
            {
                DealId = entry.DealId,
                Mode = entry.Mode,
                BetUnits = entry.BetUnits,
                ConfigVersion = entry.ConfigVersion,
                TicketCount = entry.TicketCount,
                WagersUnits = entry.WagersUnits,
                PayoutsUnits = entry.PayoutsUnits,
                TargetRtpNumerator = entry.TargetRtpNumerator,
                TargetRtpDenominator = entry.TargetRtpDenominator,
                PlannedRtpNumerator = entry.PlannedRtpDenominator == 0 ? entry.TargetRtpNumerator : entry.PlannedRtpNumerator,
                PlannedRtpDenominator = entry.PlannedRtpDenominator == 0 ? entry.TargetRtpDenominator : entry.PlannedRtpDenominator,
                CompletedUtc = now
            });

            DealEntry successor = draft.FindDeal(entry.Mode, BetLevel.FromUnits(entry.BetUnits), DealState.Ready);
            if (successor != null)
            {
                successor.State = DealState.Active;
                successor.ActivatedUtc = now;
            }
        }

        private void AppendToSpinLog(SpinRecord record)
        {
            try
            {
                m_store.AppendSpinRecord(record);
            }
            catch (Exception exception) when (IsStorageFailure(exception))
            {
                // The spin is already committed in state; the log is repaired on next start.
                m_log.Warning($"Spin {record.SpinId} committed but not yet in the spin log ({exception.Message}). It will be re-added on next start.");
            }
        }

        // ---------------------------------------------------------------- deals

        public void EnsurePrepared(GameMode mode, BetLevel bet) => TopUp(mode, bet, retryAfterFailure: true);

        public DealAvailability GetAvailability(GameMode mode, BetLevel bet)
        {
            lock (m_sync)
            {
                return GetAvailabilityLocked(mode, bet);
            }
        }

        public DealInfo GetActiveDealInfo(GameMode mode, BetLevel bet)
        {
            lock (m_sync)
            {
                DealEntry entry = m_state.FindDeal(mode, bet, DealState.Active);
                return entry == null ? null : new DealInfo(entry.DealId, entry.State, entry.ConfigVersion, entry.Cursor);
            }
        }

        /// <summary>
        /// Starts one background build if this mode/bet is missing its Active deal or its Ready
        /// successor. Each finished build calls this again, so the first call ends with both.
        /// A combo whose last build failed is only retried when asked (<paramref name="retryAfterFailure"/>),
        /// so a config that can't build doesn't rebuild after every spin.
        /// </summary>
        private void TopUp(GameMode mode, BetLevel bet, bool retryAfterFailure)
        {
            string combo = ComboKey(mode, bet);
            DealConfig config;
            lock (m_sync)
            {
                bool stocked = m_state.FindDeal(mode, bet, DealState.Active) != null && m_state.FindDeal(mode, bet, DealState.Ready) != null;
                bool failedBefore = m_unavailableReasons.ContainsKey(combo);
                if (!m_config.HasBetLevel(bet) || stocked || m_combosBeingBuilt.Contains(combo) || (failedBefore && !retryAfterFailure))
                {
                    return;
                }

                m_combosBeingBuilt.Add(combo);
                m_unavailableReasons.Remove(combo);
                config = m_config;
            }

            // Scheduled outside the lock so a slow build never holds it.
            m_scheduler.Run(() => BuildDeal(mode, bet, config));
        }

        /// <summary>
        /// Runs on a background thread. Builds and verifies outside the lock, then registers the
        /// deal as Active if the combo has none, otherwise as its Ready successor.
        /// </summary>
        private void BuildDeal(GameMode mode, BetLevel bet, DealConfig config)
        {
            string combo = ComboKey(mode, bet);
            DealCreation creation;
            try
            {
                creation = m_dealFactory.Create(config, mode, bet);
            }
            catch (Exception exception)
            {
                creation = DealCreation.Failed($"Unexpected error building {mode} {bet}: {exception.Message}");
                m_log.Error(creation.FailureMessage);
            }

            lock (m_sync)
            {
                m_combosBeingBuilt.Remove(combo);
                if (!creation.IsSuccess)
                {
                    m_unavailableReasons[combo] = creation.FailureMessage;
                    TryCommit(draft => draft.BuildFailures.Add(new BuildFailureRecord
                    {
                        Mode = mode,
                        BetUnits = bet.Amount.Units,
                        ConfigVersion = config.ConfigVersion,
                        Message = creation.FailureMessage,
                        FailedUtc = m_clock.UtcNow
                    }));
                    return;
                }

                Deal deal = creation.Deal;
                if (deal.ConfigVersion != m_config.ConfigVersion)
                {
                    // The config changed while this was building; it would be a stale successor.
                    m_log.Info($"Deal {deal.DealId} was built on config {deal.ConfigVersion}, now {m_config.ConfigVersion}; discarded and rebuilt.");
                }
                else if (!TryRegister(deal))
                {
                    return;
                }
            }

            TopUp(mode, bet, retryAfterFailure: false);
        }

        private bool TryRegister(Deal deal)
        {
            DealState state = m_state.FindDeal(deal.Mode, deal.Bet, DealState.Active) == null ? DealState.Active : DealState.Ready;
            bool registered = TryCommit(draft => draft.Deals.Add(NewEntry(deal, state)));
            if (registered)
            {
                m_loadedDeals[deal.DealId] = deal;
                m_log.Info($"Deal {deal.DealId} registered as {state} for {deal.Mode} {deal.Bet}.");
            }
            else
            {
                m_unavailableReasons[ComboKey(deal.Mode, deal.Bet)] = $"Deal {deal.DealId} was built but could not be registered.";
            }

            return registered;
        }

        private DealEntry NewEntry(Deal deal, DealState state) => new DealEntry
        {
            DealId = deal.DealId,
            Mode = deal.Mode,
            BetUnits = deal.Bet.Amount.Units,
            ConfigVersion = deal.ConfigVersion,
            State = state,
            TicketCount = deal.TicketCount,
            TargetRtpNumerator = deal.TargetRtp.Numerator,
            TargetRtpDenominator = deal.TargetRtp.Denominator,
            PlannedRtpNumerator = deal.PlannedRtp.Numerator,
            PlannedRtpDenominator = deal.PlannedRtp.Denominator,
            CreatedUtc = deal.CreatedUtc,
            ActivatedUtc = state == DealState.Active ? m_clock.UtcNow : (DateTime?)null
        };

        private DealAvailability GetAvailabilityLocked(GameMode mode, BetLevel bet)
        {
            string combo = ComboKey(mode, bet);
            DealEntry active = m_state.FindDeal(mode, bet, DealState.Active);

            if (active != null && m_loadedDeals.ContainsKey(active.DealId)) return new DealAvailability(AvailabilityStatus.Available);
            if (m_combosBeingBuilt.Contains(combo)) return new DealAvailability(AvailabilityStatus.Preparing);
            if (m_unavailableReasons.TryGetValue(combo, out string reason)) return new DealAvailability(AvailabilityStatus.Unavailable, reason);
            return new DealAvailability(AvailabilityStatus.NotPrepared);
        }

        // ---------------------------------------------------------------- admin

        public RetireRecord Retire(GameMode mode, BetLevel bet, string reason)
        {
            // Normally the Ready successor takes over. If there isn't one yet, build a replacement first,
            // outside the lock; if that fails, nothing has changed.
            Deal freshReplacement = null;
            DealConfig config;
            lock (m_sync)
            {
                if (m_state.FindDeal(mode, bet, DealState.Active) == null) throw new InvalidOperationException($"No active deal for {mode} {bet} to retire.");
                config = m_config;
            }

            if (!HasReadySuccessor(mode, bet))
            {
                DealCreation creation = m_dealFactory.Create(config, mode, bet);
                if (!creation.IsSuccess) throw new InvalidOperationException($"Retire cancelled: no replacement could be built. {creation.FailureMessage}");
                freshReplacement = creation.Deal;
            }

            RetireRecord record;
            lock (m_sync)
            {
                DealEntry active = m_state.FindDeal(mode, bet, DealState.Active)
                    ?? throw new InvalidOperationException($"No active deal for {mode} {bet} to retire.");
                DealEntry ready = m_state.FindDeal(mode, bet, DealState.Ready);
                Deal replacement = ready != null ? m_loadedDeals[ready.DealId] : freshReplacement;
                if (replacement == null) throw new InvalidOperationException($"Retire cancelled: no replacement for {mode} {bet}.");

                DateTime now = m_clock.UtcNow;
                record = CreateRetireRecord(active, reason, now, replacement.ConfigVersion, replacement.DealId);
                Commit(draft =>
                {
                    DealEntry retired = draft.FindDeal(active.DealId);
                    retired.State = DealState.Retired;
                    retired.ClosedUtc = now;
                    draft.RetiredDeals.Add(record);

                    DealEntry next = draft.FindDeal(replacement.DealId);
                    if (next == null)
                    {
                        draft.Deals.Add(NewEntry(replacement, DealState.Active));
                    }
                    else
                    {
                        next.State = DealState.Active;
                        next.ActivatedUtc = now;
                    }
                });
                m_loadedDeals[replacement.DealId] = replacement;
                m_loadedDeals.Remove(active.DealId);
                m_log.Info($"Retired deal {active.DealId} ({reason}); {replacement.DealId} is now active. Used {record.TicketsUsed}, kept {record.TicketsRemaining} unused tickets for audit.");
            }

            TopUp(mode, bet, retryAfterFailure: false);
            return record;
        }

        public void ApplyConfig(DealConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            lock (m_sync)
            {
                if (config.ConfigVersion == m_config.ConfigVersion)
                {
                    return;
                }

                m_log.Info($"Config changed from {m_config.ConfigVersion} to {config.ConfigVersion}. Active deals keep their version; the next deals use the new one.");
                Commit(draft => SwitchConfig(draft, config.ConfigVersion, m_clock.UtcNow));
                m_config = config;
                m_unavailableReasons.Clear(); // the new config may build where the old one couldn't
            }

            foreach ((GameMode mode, BetLevel bet) in CombosWithActiveDeals())
            {
                TopUp(mode, bet, retryAfterFailure: true);
            }
        }

        /// <summary>Ready successors built on an older config are retired; they were never used (A-18).</summary>
        private static void SwitchConfig(EngineState draft, string newConfigVersion, DateTime now)
        {
            draft.ConfigVersion = newConfigVersion;
            foreach (DealEntry entry in draft.Deals.Where(deal => deal.State == DealState.Ready && deal.ConfigVersion != newConfigVersion))
            {
                entry.State = DealState.Retired;
                entry.ClosedUtc = now;
                draft.RetiredDeals.Add(CreateRetireRecord(entry, $"Superseded by config {newConfigVersion}; never activated", now, newConfigVersion, null));
            }
        }

        private static RetireRecord CreateRetireRecord(DealEntry entry, string reason, DateTime now, string newConfigVersion, string replacementDealId) => new RetireRecord
        {
            DealId = entry.DealId,
            Mode = entry.Mode,
            BetUnits = entry.BetUnits,
            Reason = reason,
            RetiredUtc = now,
            OldConfigVersion = entry.ConfigVersion,
            NewConfigVersion = newConfigVersion,
            ReplacementDealId = replacementDealId,
            TicketsUsed = entry.Cursor,
            TicketsRemaining = entry.TicketCount - entry.Cursor,
            WagersUnits = entry.WagersUnits,
            PayoutsUnits = entry.PayoutsUnits
        };

        public IReadOnlyList<DealEntry> GetDeals()
        {
            lock (m_sync) return EngineStateJson.Clone(m_state).Deals;
        }

        public IReadOnlyList<CompletedDealRecord> GetCompletedDeals()
        {
            lock (m_sync) return EngineStateJson.Clone(m_state).CompletedDeals;
        }

        public IReadOnlyList<RetireRecord> GetRetiredDeals()
        {
            lock (m_sync) return EngineStateJson.Clone(m_state).RetiredDeals;
        }

        public IReadOnlyList<BuildFailureRecord> GetBuildFailures()
        {
            lock (m_sync) return EngineStateJson.Clone(m_state).BuildFailures;
        }

        private bool HasReadySuccessor(GameMode mode, BetLevel bet)
        {
            lock (m_sync) return m_state.FindDeal(mode, bet, DealState.Ready) != null;
        }

        // ---------------------------------------------------------------- players

        public bool RegisterPlayer(PlayerId player, Money startingBalance)
        {
            if (startingBalance < Money.Zero) throw new ArgumentOutOfRangeException(nameof(startingBalance), "A balance can't be negative.");

            lock (m_sync)
            {
                if (m_state.Balances.ContainsKey(player.Value))
                {
                    return false;
                }

                Commit(draft => draft.Balances[player.Value] = startingBalance.Units);
                m_log.Info($"Registered player {player} with {startingBalance}.");
                return true;
            }
        }

        public void AdjustBalance(PlayerId player, Money delta, string reason)
        {
            lock (m_sync)
            {
                if (!m_state.Balances.TryGetValue(player.Value, out long units))
                {
                    throw new InvalidOperationException($"Player '{player}' is not registered.");
                }

                Money after = Money.FromUnits(units) + delta;
                if (after < Money.Zero)
                {
                    throw new InvalidOperationException($"Adjusting {player} by {delta} would make the balance negative.");
                }

                Commit(draft => draft.Balances[player.Value] = after.Units);
                m_log.Info($"Balance of {player} adjusted by {delta} to {after}: {reason}");
            }
        }

        // ---------------------------------------------------------------- state

        /// <summary>
        /// The only way state changes: apply the change to a copy, save the copy
        /// atomically, then make it live. A failed save leaves the live state untouched.
        /// </summary>
        private void Commit(Action<EngineState> change)
        {
            EngineState draft = EngineStateJson.Clone(m_state);
            change(draft);
            m_store.WriteState(draft);
            m_state = draft;
        }

        /// <summary>Commit for bookkeeping that must not crash a background build; a failed save is logged.</summary>
        private bool TryCommit(Action<EngineState> change)
        {
            try
            {
                Commit(change);
                return true;
            }
            catch (Exception exception) when (IsStorageFailure(exception))
            {
                m_log.Error($"Could not save state: {exception.Message}");
                return false;
            }
        }

        /// <summary>
        /// State is committed before a spin is appended to the log, so after a crash the
        /// log can be at most one spin behind. Re-append that spin if it's missing.
        /// </summary>
        private void RepairSpinLog()
        {
            SpinRecord lastCommitted = m_state.LastCommittedSpin;
            if (lastCommitted == null)
            {
                return;
            }

            IReadOnlyList<SpinRecord> logged = m_store.ReadSpinRecords();
            long lastLoggedId = logged.Count == 0 ? 0 : logged[logged.Count - 1].SpinId;

            if (lastLoggedId < lastCommitted.SpinId)
            {
                m_store.AppendSpinRecord(lastCommitted);
                m_log.Warning($"Spin {lastCommitted.SpinId} was committed but missing from the spin log; it has been re-added.");
            }
            else if (lastLoggedId > lastCommitted.SpinId)
            {
                m_log.Error($"Spin log is ahead of state (log {lastLoggedId}, state {lastCommitted.SpinId}). State is treated as the truth.");
            }
        }

        /// <summary>
        /// Loads and re-verifies every Active and Ready deal. A failing Active deal is not played;
        /// a failing Ready successor is retired so it can never be switched to.
        /// </summary>
        private void LoadDeals()
        {
            foreach (DealEntry entry in m_state.Deals.Where(deal => deal.State == DealState.Active || deal.State == DealState.Ready).ToList())
            {
                string problem;
                try
                {
                    Deal deal = m_store.ReadDeal(entry.DealId);
                    VerificationReport report = DealVerifier.Verify(deal);
                    if (report.IsVerified && deal.TicketCount == entry.TicketCount && entry.Cursor <= deal.TicketCount)
                    {
                        m_loadedDeals[entry.DealId] = deal;
                        continue;
                    }

                    problem = $"{entry.State} deal {entry.DealId} failed its check on load: {report}";
                }
                catch (Exception exception) when (exception is DealFileException || IsStorageFailure(exception))
                {
                    problem = $"{entry.State} deal {entry.DealId} could not be loaded: {exception.Message}";
                }

                m_log.Error(problem);
                if (entry.State == DealState.Active)
                {
                    m_unavailableReasons[ComboKey(entry.Mode, BetLevel.FromUnits(entry.BetUnits))] = problem;
                }
                else
                {
                    Commit(draft =>
                    {
                        DealEntry ready = draft.FindDeal(entry.DealId);
                        ready.State = DealState.Retired;
                        ready.ClosedUtc = m_clock.UtcNow;
                        draft.RetiredDeals.Add(CreateRetireRecord(ready, "Failed verification on load", m_clock.UtcNow, m_config.ConfigVersion, null));
                    });
                }
            }
        }

        private List<(GameMode, BetLevel)> CombosWithActiveDeals()
        {
            lock (m_sync)
            {
                return m_state.Deals
                    .Where(deal => deal.State == DealState.Active)
                    .Select(deal => (deal.Mode, BetLevel.FromUnits(deal.BetUnits)))
                    .ToList();
            }
        }

        private static SpinResult ToResult(SpinRecord record) => new SpinResult(
            record.SpinId,
            new PlayerId(record.PlayerId),
            record.Mode,
            BetLevel.FromUnits(record.BetUnits),
            record.DealId,
            record.TicketIndex,
            Money.FromUnits(record.WagerUnits),
            Money.FromUnits(record.AwardUnits),
            record.AwardKind,
            record.Category,
            Money.FromUnits(record.BalanceAfterUnits));

        private static string ComboKey(GameMode mode, BetLevel bet) => $"{mode}:{bet.Amount.Units}";

        private static bool IsStorageFailure(Exception exception) => exception is IOException || exception is UnauthorizedAccessException;
    }
}
