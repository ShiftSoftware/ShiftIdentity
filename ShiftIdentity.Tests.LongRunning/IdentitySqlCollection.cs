using ShiftIdentity.Tests.Infrastructure;
using Xunit;

namespace ShiftIdentity.Tests;

// xUnit looks for a collection definition in the assembly that runs the tests, so this one lives here, next to the
// classes that share the "Identity SQL" database. Classes in one collection already run one after another, sharing
// this fixture's database. DisableParallelization would not add to that; it only makes xUnit hold the whole
// collection back until every other collection has finished, which turned the run into two serial halves.
[CollectionDefinition("Identity SQL")]
public sealed class IdentitySqlCollection : ICollectionFixture<SqlIdentityFixture>;
