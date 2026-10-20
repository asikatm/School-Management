using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SchoolManagement.Infrastructure.Jobs;

/// <summary>A recurring background job (spec 5.2 background work). Each run gets its own DI scope.</summary>
public interface IScheduledJob
{
    string Name { get; }
    TimeSpan Interval { get; }
    Task RunAsync(IServiceProvider services, CancellationToken ct);
}

/// <summary>Runs every registered <see cref="IScheduledJob"/> on its interval; a failing job is logged and retried next tick.</summary>
public sealed class ScheduledJobRunner(
    IEnumerable<IScheduledJob> jobs,
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    ILogger<ScheduledJobRunner> logger) : BackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextRun = jobs.ToDictionary(j => j, _ => clock.GetUtcNow());
        using var timer = new PeriodicTimer(Tick, clock);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            foreach (var job in nextRun.Keys.ToList())
            {
                if (clock.GetUtcNow() < nextRun[job]) continue;
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    await job.RunAsync(scope.ServiceProvider, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Scheduled job {Job} failed", job.Name);
                }
                nextRun[job] = clock.GetUtcNow() + job.Interval;
            }
        }
    }
}
