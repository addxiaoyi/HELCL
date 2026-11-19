using HelL.Remote.Stress;

if (args is ["--help"])
{
    Console.WriteLine("dotnet run --project tools/HelL.Remote.Stress -- --cycles 3 --pairs 1 --seed 20261118 --period 00:00:00 --timeout 00:01:00 --report artifacts/remote-stress/report.json");
    Console.WriteLine("Long soak example: --cycles 180 --pairs 2 --duration 03:00:00. Duration is minimum pacing across the finite cycle count. No public server, Docker daemon, OS credential store or existing launcher is touched.");
    return 0;
}
using var stopped = new CancellationTokenSource();
ConsoleCancelEventHandler interrupt = (_, eventArgs) => { eventArgs.Cancel = true; stopped.Cancel(); };
Console.CancelKeyPress += interrupt;
try
{
    StressReport report = await new StressRun(StressOptions.Parse(args)).RunAsync(stopped.Token);
    foreach (StressError failure in report.Errors) Console.Error.WriteLine($"Cycle {failure.Cycle}: {failure.Stage} failed ({failure.Type}).");
    Console.WriteLine($"Remote stress {report.Status}: cycles={report.CompletedCycles}; seed={report.Options.Seed}; errors={report.Errors.Count}; elapsedSeconds={report.ElapsedSeconds:F1}.");
    return report.Status == "passed" ? 0 : report.Status == "cancelled" ? 2 : 1;
}
catch (Exception failure)
{
    Console.Error.WriteLine("Remote stress setup/reporting failed (" + failure.GetType().Name + "). Use --help for supported options.");
    return 1;
}
finally { Console.CancelKeyPress -= interrupt; }
