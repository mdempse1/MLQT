using Xunit;

namespace OpenModelicaInterface.Tests;

/// <summary>
/// Collection definition to ensure all OpenModelica tests run sequentially and share the fixture.
/// </summary>
[CollectionDefinition("OpenModelica Collection", DisableParallelization = true)]
public class OpenModelicaCollection : ICollectionFixture<OpenModelicaFixture>
{
    // This class is just a marker for xUnit to group tests together
}
