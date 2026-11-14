using OpenSleigh.E2ETests.Harness.FaultInjection;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests.Scenarios;

public class CommitFailureScenario(ITestOutputHelper console) : CommitFailureScenarioBase(console)
{
    // in memory, where there is one host, and with a few hosts everywhere else: the message that follows
    // can reach another host, which only knows what is in the store
    [Theory]
    [MatrixData(Only = [Combinations.InMemory], HostsCounts = [1])]
    public Task a_commit_that_fails_does_not_leave_the_saga_locked_and_sends_none_of_its_messages(string combination, int hostsCount)
        => FailACommitAsync(combination, hostsCount);

    [Theory]
    [MatrixData(HostsCounts = [3])]
    public Task a_commit_that_fails_does_not_leave_the_saga_locked_and_sends_none_of_its_messages_with_several_hosts(string combination, int hostsCount)
        => FailACommitAsync(combination, hostsCount);

    private async Task FailACommitAsync(string combination, int hostsCount)
    {
        var outcome = await PackAParcelWithACommitThatFailsOnceAsync(combination, hostsCount);

        // the message is given back once, for the failed commit: nobody found the saga locked afterwards
        var failure = Assert.Single(outcome.Failures);
        Assert.IsType<InjectedFailureException>(failure.Exception);
        Assert.False(failure.IsLockConflict);

        // it is not stored as handled: the handler ran again for the message that followed
        Assert.Equal(1, outcome.ProcessedMessagesAfterFailedCommit);
        Assert.Equal(2, outcome.TimesPackingWasHandled);

        // and what the handler published before the commit failed never went out
        Assert.Equal(1, outcome.TimesPackedWasHandled);
    }
}
