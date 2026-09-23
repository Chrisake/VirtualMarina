namespace VirtualMarina.Core.Tests;

/// <summary>
/// Tests that change process-wide state, such as <see cref="System.Globalization.CultureInfo.DefaultThreadCurrentUICulture"/>
/// through <see cref="Api.MarinaLocalization.Culture"/>. xUnit runs the other test classes in parallel, and many of them
/// assert English text; a collection with parallelisation disabled runs on its own, after the parallel ones have
/// finished, so a culture switched here can never leak into a test running beside it.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProcessWideState
{
    /// <summary>The collection name to put in <see cref="CollectionAttribute"/>.</summary>
    public const string Name = "Process-wide state";

    // Only the attribute matters; xUnit never needs an instance, since the collection has no fixture.
    private ProcessWideState()
    {
    }
}
