using System.Collections.Generic;
using System.Linq;
using FinitePool.Engine.Core;
using FinitePool.Engine.Service;
using FinitePool.Engine.Sizing;
using FinitePool.Tests.Fakes;
using NUnit.Framework;

namespace FinitePool.Tests
{
    public class ReportToolTests
    {
        [Test]
        public void DealSizeTable_HasAHeaderAndOneRowPerModeAndBet_MatchingTheGoldenValues()
        {
            string[] lines = DealSizeTable.ToCsv(TestConfigs.Shipped()).Trim().Split('\n').Select(line => line.TrimEnd('\r')).ToArray();

            Assert.That(lines[0], Does.StartWith("# Deal-size table").And.Contain("ASSUMED bag values"));
            Assert.That(lines[1], Does.StartWith("no,mode,bet,"));
            Assert.That(lines.Length, Is.EqualTo(2 + 40));

            // Inner $1.00: 1 paired cycle, 60 spins, 6 Regular + 2 Big, 52 losses, $60 wagered, $54 paid, 90%.
            Assert.That(lines, Has.Some.EqualTo("4,Inner,1.00,29,25,54.00,1,60,6,2,8,52,60.00,54.00,90.000%,ok"));
            Assert.That(lines.Last(), Does.StartWith("40,Outer,5.00,"), "rows are numbered 1 to 40");
        }

        [Test]
        public void DealSizeTable_KeepsRejectedCombinations_WithTheirReason()
        {
            var config = TestConfigs.ShippedWith(root => root["modes"]["Inner"]["regularBag"]["tickets"][0]["multiplier"] = "0.5");

            string csv = DealSizeTable.ToCsv(config);

            Assert.That(csv, Does.Contain("1,Inner,0.25,,,,,,,,,,,,,\"rejected: FractionalCentAward\""));
        }

        [Test]
        public void Simulator_PlaysCompleteDeals_EachExactlyOnTarget()
        {
            IReadOnlyList<CompletedDealRecord> deals = DealSimulator.PlayCompleteDeals(
                TestConfigs.Shipped(), GameMode.Inner, TestConfigs.Bet(100), 3);

            Assert.That(deals, Has.Count.EqualTo(3));
            Assert.That(deals.Select(deal => deal.DealId).Distinct().Count(), Is.EqualTo(3));
            Assert.That(deals.All(deal => deal.TicketCount == GoldenDealSizes.Inner100.PaidSpins), Is.True);
            Assert.That(deals.All(deal => deal.ActualRtp == new Fraction(9, 10)), Is.True);
        }
    }
}
