using System;
using FinitePool.Engine.Core;
using NUnit.Framework;

namespace FinitePool.Tests
{
    public class MoneyTests
    {
        [Test]
        public void FromCents_And_Arithmetic_AreExact()
        {
            Money a = Money.FromCents(125);
            Money b = Money.FromCents(75);

            Assert.That((a + b).Units, Is.EqualTo(200));
            Assert.That((a - b).Units, Is.EqualTo(50));
            Assert.That((a * 3).Units, Is.EqualTo(375));
            Assert.That(a > b, Is.True);
        }

        [Test]
        public void Overflow_Throws_InsteadOfWrapping()
        {
            Money huge = Money.FromUnits(long.MaxValue);

            Assert.Throws<OverflowException>(() => { Money _ = huge + Money.FromUnits(1); });
            Assert.Throws<OverflowException>(() => { Money _ = huge * 2; });
        }

        [Test]
        public void FromDollars_RejectsFractionalCents()
        {
            Assert.That(Money.FromDollars(1.25m).Units, Is.EqualTo(125));
            Assert.Throws<ArgumentException>(() => Money.FromDollars(1.255m));
        }

        [Test]
        public void ToString_FormatsAsDollars()
        {
            Assert.That(Money.FromCents(125).ToString(), Is.EqualTo("$1.25"));
            Assert.That(Money.FromCents(-50).ToString(), Is.EqualTo("-$0.50"));
            Assert.That(Money.Zero.ToString(), Is.EqualTo("$0.00"));
        }
    }

    public class FractionTests
    {
        [Test]
        public void FromDecimal_IsExact()
        {
            Assert.That(Fraction.FromDecimal(0.90m), Is.EqualTo(new Fraction(9, 10)));
            Assert.That(Fraction.FromDecimal(0.9m), Is.EqualTo(new Fraction(9, 10)));
            Assert.That(Fraction.FromDecimal(0.125m), Is.EqualTo(new Fraction(1, 8)));
        }

        [Test]
        public void Constructor_ReducesAndNormalisesSign()
        {
            var fraction = new Fraction(6, -4);

            Assert.That(fraction.Numerator, Is.EqualTo(-3));
            Assert.That(fraction.Denominator, Is.EqualTo(2));
        }

        [Test]
        public void EqualValues_AreEqual()
        {
            Assert.That(new Fraction(5400, 6000), Is.EqualTo(new Fraction(9, 10)));
            Assert.That(new Fraction(5401, 6000), Is.Not.EqualTo(new Fraction(9, 10)));
        }

        [Test]
        public void ZeroDenominator_Throws()
        {
            Assert.Throws<ArgumentException>(() => { var _ = new Fraction(1, 0); });
        }

        [Test]
        public void ToPercentString_ShowsThreeDecimals()
        {
            Assert.That(new Fraction(9, 10).ToPercentString(), Is.EqualTo("90.000%"));
        }
    }
}
