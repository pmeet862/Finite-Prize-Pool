using System.Security.Cryptography;

namespace FinitePool.Engine.Building
{
    /// <summary>
    /// Randomness for deal building only (brief §2: spins never generate a
    /// random outcome). Injected so tests can script values; production always
    /// uses <see cref="CryptoRandomSource"/>.
    /// </summary>
    public interface IRandomSource
    {
        /// <summary>A uniformly random integer in [0, maxExclusive).</summary>
        int NextInt(int maxExclusive);
    }

    /// <summary>
    /// The only production random source (brief §3.2): a cryptographic RNG
    /// with unbiased range reduction. Thread-safe.
    /// </summary>
    public sealed class CryptoRandomSource : IRandomSource
    {
        public int NextInt(int maxExclusive) => RandomNumberGenerator.GetInt32(maxExclusive);
    }
}
