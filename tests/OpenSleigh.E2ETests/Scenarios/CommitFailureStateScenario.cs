using Xunit.Abstractions;

namespace OpenSleigh.E2ETests.Scenarios;

public class CommitFailureStateScenario(ITestOutputHelper console) : CommitFailureScenarioBase(console)
{
    // one host: what is stored does not depend on how many hosts there are
    [Theory]
    [MatrixData(HostsCounts = [1])]
    public async Task a_commit_that_fails_stores_nothing_of_what_the_handler_did_to_the_state(string combination, int hostsCount)
    {
        var outcome = await PackAParcelWithACommitThatFailsOnceAsync(combination, hostsCount);

        Assert.Equal(0, outcome.PackingAttemptsAfterFailedCommit);
        Assert.Equal(1, outcome.PackingAttemptsAtTheEnd);
    }
}
