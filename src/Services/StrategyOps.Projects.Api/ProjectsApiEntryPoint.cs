namespace StrategyOps.Projects.Api;

/// <summary>
/// Entry-point marker for WebApplicationFactory.
/// </summary>
/// <remarks>
/// Top-level statements put the generated Program class in the global namespace, so several
/// services referenced from one test project collide on the name. A namespaced marker in each
/// service assembly avoids that - WebApplicationFactory only needs some type from the
/// application's assembly, not the entry point itself.
/// </remarks>
public sealed class ProjectsApiEntryPoint;
