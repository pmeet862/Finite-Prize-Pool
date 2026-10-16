using System;

namespace FinitePool.Engine.Core
{
    /// <summary>Small integer helpers used by the exact deal-size math.</summary>
    public static class IntMath
    {
        /// <summary>Greatest common divisor of the absolute values. Gcd(0, 0) is defined as 1 to keep division safe.</summary>
        public static long Gcd(long a, long b)
        {
            a = Math.Abs(a);
            b = Math.Abs(b);
            while (b != 0)
            {
                long remainder = a % b;
                a = b;
                b = remainder;
            }

            return a == 0 ? 1 : a;
        }
    }
}
