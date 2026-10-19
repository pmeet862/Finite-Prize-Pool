using System;
using System.Globalization;
using System.Text;
using FinitePool.Engine.Config;
using FinitePool.Engine.Core;

namespace FinitePool.Engine.Sizing
{
    /// <summary>
    /// The brief §4.2 table: the calculator's result for every mode and bet level,
    /// as CSV. Rejected combinations stay in the table with their reason.
    /// </summary>
    public static class DealSizeTable
    {
        private const string HEADER =
            "no,mode,bet,multiplier_total_per_paired_cycle,bonus_units_per_paired_cycle,payout_per_paired_cycle," +
            "paired_cycles,paid_spins,regular_tickets,big_tickets,prize_tickets,losing_tickets," +
            "total_wagers,total_payouts,planned_rtp,status";

        public static string ToCsv(DealConfig config)
        {
            var csv = new StringBuilder();
            string rtpMode = config.IsToleranceMode
                ? $"tolerance mode: RTP within {config.TargetRtp.ToPercentString()} ± {(config.RtpTolerance.ToDecimal() * 100m).ToString("0.###", CultureInfo.InvariantCulture)} points"
                : $"exact mode: RTP exactly {config.TargetRtp.ToPercentString()}";
            csv.AppendLine($"# Deal-size table for config {config.ConfigVersion}, {rtpMode}{(config.IsPlaceholder ? " (PLACEHOLDER bag values)" : "")}");
            csv.AppendLine(HEADER);

            int rowNumber = 0;
            foreach (GameMode mode in Enum.GetValues(typeof(GameMode)))
            {
                foreach (BetLevel bet in config.BetLevels)
                {
                    DealSizeResult result = DealSizeCalculator.Calculate(config, mode, bet);
                    rowNumber++;
                    csv.AppendLine(rowNumber + "," + (result.IsAccepted ? Row(result.Size) : RejectedRow(result)));
                }
            }

            return csv.ToString();
        }

        private static string Row(DealSize size) => string.Join(",",
            size.Mode,
            Dollars(size.Bet.Amount),
            size.MultiplierTotalPerPairedCycle.ToString(CultureInfo.InvariantCulture),
            size.BonusUnitsPerPairedCycle,
            Dollars(size.PayoutPerPairedCycle),
            size.PairedCycles,
            size.PaidSpins,
            size.RegularTickets,
            size.BigTickets,
            size.PrizeTickets,
            size.LosingTickets,
            Dollars(size.TotalWagers),
            Dollars(size.TotalPayouts),
            size.ActualRtp.ToPercentString(),
            "ok");

        private static string RejectedRow(DealSizeResult result) =>
            $"{result.Mode},{Dollars(result.Bet.Amount)},,,,,,,,,,,,,\"rejected: {result.Rejection}\"";

        private static string Dollars(Money money) => money.ToDollars().ToString("0.00", CultureInfo.InvariantCulture);
    }
}
