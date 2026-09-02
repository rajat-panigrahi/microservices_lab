using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using StrategyOps.BuildingBlocks.Api;
using StrategyOps.Discovery.Api.Infrastructure;

namespace StrategyOps.Discovery.Api.Features.Registry;

public sealed record RegisterInstanceCommand(string InstanceId, string ServiceName, string BaseUrl, int LeaseSeconds);

public sealed record InstanceView(string InstanceId, string ServiceName, string BaseUrl, DateTimeOffset LastHeartbeatUtc, int LeaseSeconds);

public sealed class RegisterInstanceValidator : AbstractValidator<RegisterInstanceCommand>
{
    public RegisterInstanceValidator()
    {
        RuleFor(x => x.InstanceId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ServiceName).NotEmpty().MaximumLength(60);
        RuleFor(x => x.BaseUrl).NotEmpty().MaximumLength(300);
        RuleFor(x => x.LeaseSeconds).InclusiveBetween(5, 300);
    }
}

/// <summary>
/// The registry API: register, heartbeat, look up, deregister.
/// </summary>
/// <remarks>
/// <para>
/// This is a deliberately small, readable version of what Consul or Eureka do, so the
/// mechanism is visible rather than hidden behind a product. The parts that matter are the
/// same everywhere:
/// </para>
/// <list type="bullet">
///   <item><b>Register</b> on startup, <b>heartbeat</b> to keep the lease, <b>deregister</b>
///   on clean shutdown, and get <b>evicted</b> if you stop answering.</item>
///   <item>Lookup returns <b>a list</b>, not one address - that is what makes client-side
///   load balancing and rolling deploys possible.</item>
/// </list>
/// <para>
/// On Kubernetes you would usually not run any of this: a Service gives you a stable DNS name
/// and kube-proxy load-balances behind it, with readiness probes playing the role of
/// heartbeats. Knowing that the platform already solves this - and being able to say what it
/// is solving - is the point of building it once by hand.
/// </para>
/// </remarks>
public sealed class RegistryEndpoints : IEndpoint
{
    public void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/registry").WithTags("Registry").AllowAnonymous();

        group.MapPost("/instances", (RegisterInstanceCommand command, ServiceRegistry registry) =>
            {
                var instance = registry.Register(command.InstanceId, command.ServiceName, command.BaseUrl, command.LeaseSeconds);
                return Results.Ok(ToView(instance));
            })
            .WithName("RegisterInstance")
            .WithSummary("Register an instance and take out a lease")
            .WithValidation<RegisterInstanceCommand>()
            .Produces<InstanceView>();

        group.MapPut("/instances/{instanceId}/heartbeat", (string instanceId, ServiceRegistry registry) =>
                registry.Heartbeat(instanceId)
                    ? Results.NoContent()

                    // 404 rather than 204 so a caller whose lease was reaped finds out and can
                    // re-register, instead of quietly heartbeating into the void forever.
                    : Results.Problem(
                        title: "Resource was not found",
                        detail: $"Instance '{instanceId}' is not registered; register again.",
                        statusCode: StatusCodes.Status404NotFound))
            .WithName("Heartbeat")
            .WithSummary("Renew an instance's lease");

        group.MapDelete("/instances/{instanceId}", (string instanceId, ServiceRegistry registry) =>
                registry.Deregister(instanceId) ? Results.NoContent() : Results.NoContent())
            .WithName("Deregister")
            .WithSummary("Remove an instance on clean shutdown");

        group.MapGet("/services/{serviceName}", (string serviceName, ServiceRegistry registry) =>
                Results.Ok(registry.Healthy(serviceName).Select(ToView).ToList()))
            .WithName("LookupService")
            .WithSummary("Live instances of one service")
            .Produces<List<InstanceView>>();

        group.MapGet("/services", (ServiceRegistry registry) =>
                Results.Ok(registry.All().Select(ToView).ToList()))
            .WithName("ListRegistry")
            .WithSummary("Everything currently registered")
            .Produces<List<InstanceView>>();
    }

    private static InstanceView ToView(Domain.ServiceInstance instance) =>
        new(instance.InstanceId, instance.ServiceName, instance.BaseUrl, instance.LastHeartbeatUtc, instance.LeaseSeconds);
}
