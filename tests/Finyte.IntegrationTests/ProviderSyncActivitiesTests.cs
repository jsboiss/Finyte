using Finyte.Data.ProviderSync;
using Microsoft.Extensions.DependencyInjection;
using Temporalio.Testing;
using Xunit;

namespace Finyte.IntegrationTests;

public sealed class ProviderSyncActivitiesTests
{
    [Fact]
    public async Task LongSyncKeepsHeartbeatingUntilItFinishes()
    {
        var runner = new SlowRunner(TimeSpan.FromMilliseconds(400));
        await using var services = new ServiceCollection().AddSingleton<IProviderSyncRunner>(runner).BuildServiceProvider();
        var activities = new ProviderSyncActivities(services.GetRequiredService<IServiceScopeFactory>(), TimeSpan.FromMilliseconds(50));
        var heartbeats = 0;
        var environment = new ActivityEnvironment { Heartbeater = _ => Interlocked.Increment(ref heartbeats) };
        var syncRunId = Guid.NewGuid();

        await environment.RunAsync(() => activities.Run(syncRunId));
        var afterRun = Volatile.Read(ref heartbeats);
        await Task.Delay(200);

        Assert.Equal(syncRunId, runner.SyncRunId);
        Assert.True(afterRun >= 4, $"Expected repeated heartbeats during the sync, got {afterRun}.");
        Assert.Equal(afterRun, Volatile.Read(ref heartbeats));
    }

    [Fact]
    public async Task FailedSyncStopsHeartbeatingAndRethrows()
    {
        var runner = new SlowRunner(TimeSpan.FromMilliseconds(100), fail: true);
        await using var services = new ServiceCollection().AddSingleton<IProviderSyncRunner>(runner).BuildServiceProvider();
        var activities = new ProviderSyncActivities(services.GetRequiredService<IServiceScopeFactory>(), TimeSpan.FromMilliseconds(20));
        var heartbeats = 0;
        var environment = new ActivityEnvironment { Heartbeater = _ => Interlocked.Increment(ref heartbeats) };

        await Assert.ThrowsAsync<InvalidOperationException>(() => environment.RunAsync(() => activities.Run(Guid.NewGuid())));
        var afterRun = Volatile.Read(ref heartbeats);
        await Task.Delay(100);

        Assert.Equal(afterRun, Volatile.Read(ref heartbeats));
    }

    [Fact]
    public void WorkerCanResolveTheActivitiesWithTheDefaultInterval()
    {
        using var services = new ServiceCollection().AddSingleton<ProviderSyncActivities>().BuildServiceProvider();

        Assert.NotNull(services.GetRequiredService<ProviderSyncActivities>());
    }

    private sealed class SlowRunner(TimeSpan duration, bool fail = false) : IProviderSyncRunner
    {
        public Guid? SyncRunId { get; private set; }

        public async Task Run(Guid syncRunId, CancellationToken cancellationToken)
        {
            SyncRunId = syncRunId;
            await Task.Delay(duration, cancellationToken);
            if (fail)
            {
                throw new InvalidOperationException("sync failed");
            }
        }
    }
}
