using System;
using System.IO;
using FinitePool.Engine.Building;
using FinitePool.Engine.Config;
using FinitePool.Engine.Core;
using FinitePool.Engine.Deals;
using FinitePool.Engine.Infrastructure;
using FinitePool.Engine.Persistence;
using FinitePool.Engine.Verification;

namespace FinitePool.Engine.Lifecycle
{
    /// <summary>
    /// Produces a deal that is safe to play (brief §4.3 steps 4–5):
    ///   build it → save the complete ordered deal → read it back from storage →
    ///   verify the copy that was read back.
    /// Verifying the saved copy, not the in-memory one, proves what is on disk is right.
    /// Touches only its own new deal file, so it can run on a background thread.
    /// </summary>
    internal sealed class VerifiedDealFactory
    {
        private readonly DealBuilder m_builder;
        private readonly IDealStore m_store;
        private readonly IEngineLog m_log;

        public VerifiedDealFactory(DealBuilder builder, IDealStore store, IEngineLog log)
        {
            m_builder = builder ?? throw new ArgumentNullException(nameof(builder));
            m_store = store ?? throw new ArgumentNullException(nameof(store));
            m_log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public DealCreation Create(DealConfig config, GameMode mode, BetLevel bet)
        {
            BuildResult build = m_builder.Build(config, mode, bet);
            if (!build.IsSuccess)
            {
                return DealCreation.Failed(build.Message);
            }

            Deal saved;
            try
            {
                m_store.WriteDeal(build.Deal);
                saved = m_store.ReadDeal(build.Deal.DealId);
            }
            catch (Exception exception) when (exception is IOException || exception is DealFileException || exception is UnauthorizedAccessException)
            {
                string message = $"Deal {build.Deal.DealId} could not be saved and read back: {exception.Message}";
                m_log.Error(message);
                return DealCreation.Failed(message);
            }

            VerificationReport report = DealVerifier.Verify(saved);
            if (!report.IsVerified)
            {
                // Never used (A-15). The file is left in place for investigation.
                m_log.Error(report.ToString());
                return DealCreation.Failed(report.ToString());
            }

            m_log.Info(report.ToString());
            return DealCreation.Succeeded(saved);
        }
    }

    internal sealed class DealCreation
    {
        public Deal Deal { get; }
        public string FailureMessage { get; }
        public bool IsSuccess => Deal != null;

        private DealCreation(Deal deal, string failureMessage)
        {
            Deal = deal;
            FailureMessage = failureMessage;
        }

        public static DealCreation Succeeded(Deal deal) => new DealCreation(deal, null);

        public static DealCreation Failed(string message) => new DealCreation(null, message);
    }
}
