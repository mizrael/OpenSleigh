namespace OpenSleigh.Tests;

internal class FakeSagaNoStarter : Saga
{
    public FakeSagaNoStarter(ISagaInstance context) : base(context)
    {
    }
}