using System;
using FinitePool.Engine.Config;
using FinitePool.Engine.Core;
using FinitePool.Engine.Deals;
using FinitePool.Engine.Infrastructure;
using FinitePool.Engine.Placement;
using FinitePool.Engine.Sizing;

namespace FinitePool.Engine.Building
{
    /// <summary>
    /// Builds one deal in memory (brief §4.3 steps 1–3):
    ///   1. size it with the calculator and work out the placement limits,
    ///   2. reject impossible limits up front (no attempts spent),
    ///   3. shuffle each Regular and Big cycle on its own and append them in order,
    ///   4. generate category layouts until one passes the validator, at most
    ///      MaxGenerationAttempts times; then reject and log, never loop forever
    ///      or loosen the limits,
    ///   5. merge: each Regular slot takes the next Regular ticket, each Big slot
    ///      the next Big ticket, Loss slots take the Loss ticket.
    /// Saving and verifying the deal (steps 4–5) happen after this, in the lifecycle code.
    ///
    /// Pure and synchronous: no shared state, safe to run on a background thread.
    /// </summary>
    internal sealed class DealBuilder
    {
        private readonly IRandomSource m_random;
        private readonly IClock m_clock;
        private readonly IEngineLog m_log;
        private readonly ICategoryLayoutGenerator m_layoutGeneratorOverride;

        /// <param name="layoutGeneratorOverride">Tests only: replaces the config's layout strategy.</param>
        public DealBuilder(IRandomSource random, IClock clock, IEngineLog log, ICategoryLayoutGenerator layoutGeneratorOverride = null)
        {
            m_random = random ?? throw new ArgumentNullException(nameof(random));
            m_clock = clock ?? throw new ArgumentNullException(nameof(clock));
            m_log = log ?? throw new ArgumentNullException(nameof(log));
            m_layoutGeneratorOverride = layoutGeneratorOverride;
        }

        public BuildResult Build(DealConfig config, GameMode mode, BetLevel bet)
        {
            DealSizeResult sizeResult = DealSizeCalculator.Calculate(config, mode, bet);
            if (!sizeResult.IsAccepted)
            {
                return Fail(config, mode, bet, BuildFailureReason.SizeRejected, sizeResult.Message, 0, null);
            }

            DealSize size = sizeResult.Size;
            PlacementLimits limits = PlacementLimits.For(size, config.PlacementLimits);

            string impossibility = PlacementFeasibility.FindImpossibility(size, limits);
            if (impossibility != null)
            {
                return Fail(config, mode, bet, BuildFailureReason.LimitsImpossible, impossibility, 0, limits);
            }

            ModeConfig modeConfig = config.GetMode(mode);
            var ticketTypes = TicketTypeTable.Create(modeConfig, bet, config.BonusUnitValue);
            ushort[] regularTickets = CycleSequenceBuilder.Build(
                TicketTypeTable.CycleTemplate(ticketTypes, TicketCategory.Regular), size.PairedCycles, m_random);
            ushort[] bigTickets = CycleSequenceBuilder.Build(
                TicketTypeTable.CycleTemplate(ticketTypes, TicketCategory.Big), size.PairedCycles, m_random);

            ICategoryLayoutGenerator generator = m_layoutGeneratorOverride
                ?? CategoryLayoutGenerators.For(config.PlacementLimits.LayoutStrategy);
            string lastProblem = "no attempts made";

            for (int attempt = 1; attempt <= config.MaxGenerationAttempts; attempt++)
            {
                TicketCategory[] layout = generator.TryGenerate(
                    (int)size.RegularTickets, (int)size.BigTickets, (int)size.LosingTickets, limits, m_random);
                if (layout == null)
                {
                    lastProblem = "the layout generator reached a dead end";
                    continue;
                }

                PlacementReport report = PlacementValidator.Validate(layout, limits);
                if (!report.IsValid)
                {
                    lastProblem = report.ToString();
                    continue;
                }

                var deal = new Deal(
                    Deal.NewId(mode, bet),
                    mode,
                    bet,
                    config.ConfigVersion,
                    m_clock.UtcNow,
                    config.TargetRtp,
                    size.PairedCycles,
                    config.BonusUnitValue,
                    limits,
                    ticketTypes,
                    Merge(layout, regularTickets, bigTickets));

                m_log.Info($"Built {deal} after {attempt} attempt(s). Layout {report}.");
                return BuildResult.Succeeded(deal, size, limits, report, attempt);
            }

            return Fail(config, mode, bet, BuildFailureReason.AttemptsExhausted,
                $"No valid layout in {config.MaxGenerationAttempts} attempts. Last problem: {lastProblem}.",
                config.MaxGenerationAttempts, limits);
        }

        /// <summary>Brief §4.3 step 3. Regular and Big tickets keep their cycle order; only the slots are interleaved.</summary>
        private static ushort[] Merge(TicketCategory[] layout, ushort[] regularTickets, ushort[] bigTickets)
        {
            var merged = new ushort[layout.Length];
            int nextRegular = 0, nextBig = 0;

            for (int slot = 0; slot < layout.Length; slot++)
            {
                switch (layout[slot])
                {
                    case TicketCategory.Regular:
                        merged[slot] = regularTickets[nextRegular++];
                        break;
                    case TicketCategory.Big:
                        merged[slot] = bigTickets[nextBig++];
                        break;
                    default:
                        merged[slot] = TicketTypeTable.LOSS_ID;
                        break;
                }
            }

            if (nextRegular != regularTickets.Length || nextBig != bigTickets.Length)
            {
                throw new InvalidOperationException("Layout counts don't match the ticket streams.");
            }

            return merged;
        }

        private BuildResult Fail(DealConfig config, GameMode mode, BetLevel bet, BuildFailureReason reason, string message, int attempts, PlacementLimits limits)
        {
            string fullMessage = $"Deal build rejected for {mode} {bet} (config {config.ConfigVersion}, {reason}): {message}";
            m_log.Error(fullMessage);
            return BuildResult.Failed(reason, fullMessage, attempts, limits);
        }
    }

    internal enum BuildFailureReason
    {
        /// <summary>The calculator found no exact deal for this mode/bet.</summary>
        SizeRejected,

        /// <summary>The placement limits can never be met (checked before any attempt).</summary>
        LimitsImpossible,

        /// <summary>Every allowed attempt produced an invalid layout.</summary>
        AttemptsExhausted
    }

    /// <summary>Either a built deal, or the reason none could be built.</summary>
    internal sealed class BuildResult
    {
        public Deal Deal { get; }
        public DealSize Size { get; }
        public PlacementLimits Limits { get; }
        public PlacementReport Placement { get; }
        public int Attempts { get; }
        public BuildFailureReason? FailureReason { get; }
        public string Message { get; }

        public bool IsSuccess => Deal != null;

        private BuildResult(Deal deal, DealSize size, PlacementLimits limits, PlacementReport placement, int attempts, BuildFailureReason? failureReason, string message)
        {
            Deal = deal;
            Size = size;
            Limits = limits;
            Placement = placement;
            Attempts = attempts;
            FailureReason = failureReason;
            Message = message;
        }

        public static BuildResult Succeeded(Deal deal, DealSize size, PlacementLimits limits, PlacementReport placement, int attempts) =>
            new BuildResult(deal, size, limits, placement, attempts, null, $"Built {deal} in {attempts} attempt(s).");

        public static BuildResult Failed(BuildFailureReason reason, string message, int attempts, PlacementLimits limits) =>
            new BuildResult(null, null, limits, null, attempts, reason, message);

        public override string ToString() => Message;
    }
}
