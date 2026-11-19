using System.Globalization;

namespace HelL.Remote.Stress;

public sealed record StressOptions(int Cycles, int Pairs, int Seed, TimeSpan Period, TimeSpan Timeout, string Report)
{
    public static StressOptions Parse(string[] args)
    {
        int cycles = 3, pairs = 1, seed = 20261118;
        TimeSpan period = TimeSpan.Zero, timeout = TimeSpan.FromSeconds(60);
        TimeSpan? duration = null;
        bool periodSpecified = false;
        string report = Path.GetFullPath("artifacts/remote-stress/report.json");
        for (int index = 0; index < args.Length; index += 2)
        {
            if (index + 1 == args.Length) throw new ArgumentException("Every stress option requires a value.");
            string value = args[index + 1];
            switch (args[index])
            {
                case "--cycles": cycles = int.Parse(value, CultureInfo.InvariantCulture); break;
                case "--pairs": pairs = int.Parse(value, CultureInfo.InvariantCulture); break;
                case "--seed": seed = int.Parse(value, CultureInfo.InvariantCulture); break;
                case "--period": period = TimeSpan.Parse(value, CultureInfo.InvariantCulture); periodSpecified = true; break;
                case "--duration": duration = TimeSpan.Parse(value, CultureInfo.InvariantCulture); break;
                case "--timeout": timeout = TimeSpan.Parse(value, CultureInfo.InvariantCulture); break;
                case "--report": report = Path.GetFullPath(value); break;
                default: throw new ArgumentException("Unknown stress option.");
            }
        }
        if (cycles is < 1 or > 1000 || pairs is < 1 or > 8 || (long)cycles * pairs > 1000)
            throw new ArgumentOutOfRangeException(nameof(args), "Use 1-8 pairs and at most 1,000 total pairing generations.");
        if (duration is not null)
        {
            if (periodSpecified || duration <= TimeSpan.Zero || duration > TimeSpan.FromHours(24))
                throw new ArgumentException("Use either a positive total duration up to 24 hours or a cycle period, not both.");
            period = TimeSpan.FromTicks(duration.Value.Ticks / cycles);
        }
        if (period < TimeSpan.Zero || period.TotalHours * cycles > 24 || timeout < TimeSpan.FromSeconds(5) || timeout > TimeSpan.FromMinutes(5))
            throw new ArgumentOutOfRangeException(nameof(args), "Use a nonnegative cycle period up to 24 hours and a 5-second to 5-minute stage timeout.");
        return new(cycles, pairs, seed, period, timeout, report);
    }
}
