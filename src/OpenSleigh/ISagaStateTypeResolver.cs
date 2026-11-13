namespace OpenSleigh;

/// <summary>
/// knows the state types of the registered sagas, and nothing else: a name that is not one of theirs resolves to nothing.
/// </summary>
public interface ISagaStateTypeResolver
{
    /// <param name="stateTypeName">the full name of a state type, as it is stored with a saga.</param>
    /// <returns>the state type, or null when no registered saga has a state of that type.</returns>
    Type? Resolve(string stateTypeName);
}
