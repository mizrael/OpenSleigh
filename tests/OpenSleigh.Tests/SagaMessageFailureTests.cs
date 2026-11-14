namespace OpenSleigh.Tests;

public class SagaMessageFailureTests
{
    private static readonly SagaInstance Saga = new("instance-1", "trigger-1", "correlation-1", SagaDescriptor.Create<FakeSaga>());
    private static readonly FakeMessageContext<FakeSagaStarter> Message = FakeMessageContext<FakeSagaStarter>.Create(new FakeSagaStarter());

    private static Exception Thrown(string message)
    {
        try
        {
            throw new InvalidOperationException(message);
        }
        catch (Exception e)
        {
            return e;
        }
    }

    [Fact]
    public void Create_should_describe_the_saga_the_message_and_the_exception()
    {
        var before = DateTimeOffset.UtcNow;

        var failure = SagaMessageFailure.Create(Saga, Message, Thrown("whoops"), attempt: 3, FailureDecision.DeadLettered);

        Assert.NotEmpty(failure.Id);
        Assert.Equal("instance-1", failure.InstanceId);
        Assert.Equal("correlation-1", failure.CorrelationId);
        Assert.Equal(typeof(FakeSaga).FullName, failure.SagaType);
        Assert.Equal(Message.MessageId, failure.MessageId);
        Assert.Equal(typeof(FakeSagaStarter).FullName, failure.MessageType);
        Assert.Equal(3, failure.Attempt);
        Assert.InRange(failure.OccurredAt, before, DateTimeOffset.UtcNow);
        Assert.Equal(typeof(InvalidOperationException).FullName, failure.ExceptionType);
        Assert.Equal("whoops", failure.ExceptionMessage);
        Assert.Contains(nameof(Thrown), failure.StackTrace);
        Assert.Equal(FailureDecision.DeadLettered, failure.Decision);
        Assert.Null(failure.RetryAt);
        Assert.Null(failure.CompensationError);
    }

    [Fact]
    public void Create_should_give_every_failure_its_own_id()
    {
        var first = SagaMessageFailure.Create(Saga, Message, Thrown("whoops"), attempt: 1, FailureDecision.DeadLettered);
        var second = SagaMessageFailure.Create(Saga, Message, Thrown("whoops"), attempt: 1, FailureDecision.DeadLettered);

        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public void Create_should_truncate_the_details_of_a_compensation_error()
    {
        var compensationError = Thrown(new string('x', SagaMessageFailure.MaxErrorDetailsLength * 2));

        var failure = SagaMessageFailure.Create(
            Saga, Message, Thrown("whoops"), attempt: 1, FailureDecision.CompensationFailed, compensationError);

        Assert.Equal(SagaMessageFailure.MaxErrorDetailsLength, failure.CompensationError!.Length);
        Assert.StartsWith(typeof(InvalidOperationException).FullName!, failure.CompensationError);
    }

    [Fact]
    public void Create_should_refuse_an_attempt_lower_than_one()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => SagaMessageFailure.Create(Saga, Message, Thrown("whoops"), attempt: 0, FailureDecision.DeadLettered));
    }
}
