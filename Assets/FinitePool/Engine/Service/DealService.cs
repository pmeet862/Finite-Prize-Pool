using System;
using System.Collections.Generic;
using System.IO;
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
    /// - All changes happen under one lock, so a background build finishing at the
    ///   same moment as a spin can't interleave with it.
    /// </summary>
    public sealed class DealService : IDealService, IDealAdmin, IDealDiagnostics
    {
        private readonly object m_sync = new object();
        private readonly DealConfig m_config;
        private readonly IDealStore m_store;
        private readonly IClock m_clock;
        private readonly IEngineLog m_log;
        private readonly IBackgroundScheduler m_scheduler;
        private readonly VerifiedDealFactory m_dealFactory;

        // Not saved: rebuilt from state and deal files at startup.
        private readonly Dictionary<string, Deal> m_loadedDeals = new Dictionary<string, Deal>();
        private readonly HashSet<string> m_combosBeingBuilt = new HashSet<string>();
        private readonly Dictionary<string, string> m_unavailableReasons = new Dictionary<string, string>();

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
            LoadActiveDeals();
        }

        /// <summary>The game's setup: files under <paramref name="rootFolder"/>, crypto shuffles, background builds.</summary>
        public static DealService OpenWithFiles(DealConfig config, string rootFolder, IEngineLog log) =>
            new DealService(config, new FileDealStore(rootFolder), new CryptoRandomSource(), new SystemClock(), log, new ThreadPoolScheduler());

        /// <summary>Editor tools (simulator): memory only, builds run immediately.</summary>
        public static DealService OpenInMemory(DealConfig config, IEngineLog log) =>
            new DealService(config, new InMemoryDealStore(), new CryptoRandomSource(), new SystemClock(), log, new ImmediateScheduler());

        public DealConfig Config => m_config;

        // ---------------------------------------------------------------- spins

        public SpinOutcome Spin(PlayerId player, GameMode mode, BetLevel bet)
        {
            lock (m_sync)
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

                AppendToSpinLog(record);
                return SpinOutcome.Accepted(ToResult(record));
            }
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
                // Phase 5 adds the completion record and the switch to the Ready successor.
                entry.State = DealState.Completed;
                entry.ClosedUtc = record.TimestampUtc;
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

        public void EnsurePrepared(GameMode mode, BetLevel bet)
        {
            string combo = ComboKey(mode, bet);
            lock (m_sync)
            {
                if (!m_config.HasBetLevel(bet) || m_state.FindDeal(mode, bet, DealState.Active) != null || m_combosBeingBuilt.Contains(combo))
                {
                    return;
                }

                m_combosBeingBuilt.Add(combo);
                m_unavailableReasons.Remove(combo);
            }

            // Scheduled outside the lock so a slow build never holds it.
            m_scheduler.Run(() => BuildFirstDeal(mode, bet));
        }

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

        /// <summary>Runs on a background thread. Builds and verifies outside the lock; registers inside it.</summary>
        private void BuildFirstDeal(GameMode mode, BetLevel bet)
        {
            string combo = ComboKey(mode, bet);
            DealCreation creation;
            try
            {
                creation = m_dealFactory.Create(m_config, mode, bet);
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
                    return;
                }

                Deal deal = creation.Deal;
                try
                {
                    Commit(draft => draft.Deals.Add(new DealEntry
                    {
                        DealId = deal.DealId,
                        Mode = deal.Mode,
                        BetUnits = deal.Bet.Amount.Units,
                        ConfigVersion = deal.ConfigVersion,
                        State = DealState.Active,
                        TicketCount = deal.TicketCount,
                        CreatedUtc = deal.CreatedUtc,
                        ActivatedUtc = m_clock.UtcNow
                    }));
                    m_loadedDeals[deal.DealId] = deal;
                    m_log.Info($"Deal {deal.DealId} is now active for {mode} {bet}.");
                }
                catch (Exception exception) when (IsStorageFailure(exception))
                {
                    m_unavailableReasons[combo] = $"Deal {deal.DealId} was built but could not be registered: {exception.Message}";
                    m_log.Error(m_unavailableReasons[combo]);
                }
            }
        }

        private DealAvailability GetAvailabilityLocked(GameMode mode, BetLevel bet)
        {
            string combo = ComboKey(mode, bet);
            DealEntry active = m_state.FindDeal(mode, bet, DealState.Active);

            if (active != null && m_loadedDeals.ContainsKey(active.DealId)) return new DealAvailability(AvailabilityStatus.Available);
            if (m_combosBeingBuilt.Contains(combo)) return new DealAvailability(AvailabilityStatus.Preparing);
            if (m_unavailableReasons.TryGetValue(combo, out string reason)) return new DealAvailability(AvailabilityStatus.Unavailable, reason);
            return new DealAvailability(AvailabilityStatus.NotPrepared);
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

        /// <summary>Loads and re-verifies every active deal. A deal that fails is not played.</summary>
        private void LoadActiveDeals()
        {
            foreach (DealEntry entry in m_state.Deals)
            {
                if (entry.State != DealState.Active)
                {
                    continue;
                }

                string combo = ComboKey(entry.Mode, BetLevel.FromUnits(entry.BetUnits));
                try
                {
                    Deal deal = m_store.ReadDeal(entry.DealId);
                    VerificationReport report = DealVerifier.Verify(deal);
                    if (!report.IsVerified || deal.TicketCount != entry.TicketCount || entry.Cursor > deal.TicketCount)
                    {
                        m_unavailableReasons[combo] = $"Active deal {entry.DealId} failed its check on load: {report}";
                        m_log.Error(m_unavailableReasons[combo]);
                        continue;
                    }

                    m_loadedDeals[entry.DealId] = deal;
                }
                catch (Exception exception) when (exception is DealFileException || IsStorageFailure(exception))
                {
                    m_unavailableReasons[combo] = $"Active deal {entry.DealId} could not be loaded: {exception.Message}";
                    m_log.Error(m_unavailableReasons[combo]);
                }
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
