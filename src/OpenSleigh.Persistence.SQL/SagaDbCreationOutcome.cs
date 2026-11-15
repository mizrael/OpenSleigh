namespace OpenSleigh.Persistence.SQL;

internal enum SagaDbCreationOutcome
{
    /// <summary>the tables were there: the database was created earlier, maybe by an earlier version.</summary>
    AlreadyThere,

    /// <summary>this call created the tables.</summary>
    Created,

    /// <summary>the tables were missing, and another instance created them while this call waited for it.</summary>
    CreatedByAnotherInstance,
}
