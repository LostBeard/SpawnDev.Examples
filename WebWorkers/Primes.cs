namespace WebWorkers;

public static class Primes
{
    /// <summary>Counts the primes up to and including <paramref name="limit"/>. Slow on purpose.</summary>
    public static int Count(int limit)
    {
        var count = 0;
        for (var n = 2; n <= limit; n++)
        {
            var isPrime = true;
            for (var d = 2; (long)d * d <= n; d++)
            {
                if (n % d == 0)
                {
                    isPrime = false;
                    break;
                }
            }
            if (isPrime) count++;
        }
        return count;
    }
}
