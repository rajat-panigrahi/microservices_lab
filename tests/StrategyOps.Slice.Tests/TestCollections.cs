namespace StrategyOps.Slice.Tests;

/// <summary>
/// One service host shared across a class's tests. Each test uses distinct project codes so
/// they stay independent without paying to rebuild the host every time.
/// </summary>
[CollectionDefinition(nameof(ProjectsApiCollection))]
public sealed class ProjectsApiCollection : ICollectionFixture<ProjectsApiFactory>;
