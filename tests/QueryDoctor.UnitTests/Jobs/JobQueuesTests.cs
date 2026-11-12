using QueryDoctor.Application.Jobs;

namespace QueryDoctor.UnitTests.Jobs;

public class JobQueuesTests
{
    [Fact]
    public void QueuesFollowDocumentedPriorityOrder()
    {
        // El documento de arquitectura (§5.3 y DA-03) estipula el orden estricto de colas:
        // probe, ingest, analyze, explain, retention, default
        var expectedOrder = new[]
        {
            JobQueues.Probe,
            JobQueues.Ingest,
            JobQueues.Analyze,
            JobQueues.Explain,
            JobQueues.Retention,
            JobQueues.Default
        };

        Assert.Equal(expectedOrder, JobQueues.AllInPriorityOrder);
    }

    [Fact]
    public void QueueNamesAreLowerCaseAndNotEmpty()
    {
        foreach (var queue in JobQueues.AllInPriorityOrder)
        {
            Assert.False(string.IsNullOrWhiteSpace(queue));
            Assert.Equal(queue.ToLowerInvariant(), queue);
        }
    }
}
