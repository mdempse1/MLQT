using Xunit;

namespace DymolaInterface.Tests;

/// <summary>
/// Collection definition to ensure all Dymola tests run sequentially and share the fixture.
/// </summary>
[CollectionDefinition("Dymola Collection", DisableParallelization = true)]
public class DymolaCollection : ICollectionFixture<DymolaFixture>
{
    // This class is just a marker for xUnit to group tests together
}
