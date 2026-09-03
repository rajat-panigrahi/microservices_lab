# 22. How do Docker and Kubernetes help microservices?

*Asked by: Cognizant, Infosys, Capgemini, TCS*

## The 60-second answer

> **Docker** solves packaging: one image contains the app and everything it needs, so it runs
> identically on my laptop, in CI and in production. That matters far more with nine services
> than with one — nine services meant nine machines to configure identically before containers.
>
> **Kubernetes** solves running them: scheduling containers onto machines, restarting the ones
> that die, load balancing across replicas, rolling out new versions without downtime, and
> giving each service a stable DNS name.
>
> The part worth stressing is how much Kubernetes **takes over from the application**. My
> service registry is not deployed to Kubernetes at all — a Service gives each deployment a
> stable name and load-balances across its pods, doing the registry's job better, with no code.
> Building the registry by hand first is how you learn what the platform is actually doing.

## What Docker gives you

- **Identical environments.** "Works on my machine" becomes "here is the machine."
- **Isolation.** Each service brings its own runtime; one can be on .NET 8 and another on .NET 10.
- **Fast startup.** Seconds, not minutes — which is what makes autoscaling and rolling deploys
  practical.
- **Immutable artifacts.** The image that passed tests is the image that ships.

Three things in my Dockerfile that are worth defending:

1. **Multi-stage build** — the SDK builds, the ASP.NET runtime image runs. About a fifth of the
   size, and it does not ship a compiler into production.
2. **Restore layer cached separately** — copy `.csproj` files, restore, *then* copy source. A
   code change does not re-download every package. The single biggest win in .NET image build
   times.
3. **Non-root user** — a container running as root turns a code-execution bug into a host-level
   one, and it is a one-line change.

## What Kubernetes gives you

| Concept | Job | Replaces |
|---|---|---|
| **Pod** | one or more containers scheduled together | — |
| **Deployment** | desired replica count, rolling updates | manual deploy scripts |
| **Service** | stable DNS name + load balancing | **my service registry** |
| **Ingress** | external traffic routing | a load balancer config |
| **ConfigMap / Secret** | config and credentials | config files in the image |
| **HPA** | scale on CPU or custom metrics | manual capacity planning |
| **Liveness / readiness probes** | restart the wedged, drain the not-ready | health monitoring scripts |

## The probes are the detail interviewers push on

This is where the application and the platform have to agree:

| | Question | On failure | May check dependencies? |
|---|---|---|---|
| **Liveness** | is this process wedged? | pod is **killed** | **No** |
| **Readiness** | can I serve traffic now? | removed from the Service | Yes |

If liveness checked the database, one database blip would restart **every replica you have**,
turning a brief degradation into an outage. That is why `/health` in this repo checks nothing
and `/health/ready` carries the database check. A **startup probe** additionally buys slow
starters time — EF migrations on boot, here — without loosening liveness for the pod's whole
life.

`maxUnavailable: 0` then makes rollouts invisible: new pods must become *ready* before old ones
are removed. That only works because readiness is honest.

## In this repo

- [`deploy/docker/Dockerfile`](../../deploy/docker/Dockerfile) — one file, all nine services
- [`deploy/docker-compose.yml`](../../deploy/docker-compose.yml) — services plus RabbitMQ, PostgreSQL, Seq, Jaeger
- [`deploy/k8s/strategyops.yaml`](../../deploy/k8s/strategyops.yaml) — 22 documents; note `Discovery__Enabled: "false"`

**Not executed here** — no Docker daemon, no cluster. Both YAMLs parse; expect to fix something
on first run.

## Follow-up probes

**"Do you need Kubernetes for microservices?"**
> No. Nine services run fine on VMs with systemd, or on Azure Container Apps, AWS ECS, or
> Nomad. Kubernetes is powerful and genuinely complex, and adopting it *and* microservices at
> once is two hard migrations at the same time. If a managed container platform covers your
> needs, it is usually the better trade.

**"How does a service find another one in Kubernetes?"**
> DNS. A Service named `projects-api` is reachable at `http://projects-api:8080` from any pod
> in the namespace, and kube-proxy load-balances across the ready pods. That is exactly why the
> registry is switched off there.

**"What is a service mesh, and do you need one?"**
> Istio or Linkerd put a sidecar proxy next to every pod, giving mTLS, retries, timeouts,
> traffic splitting and traces **without application code**. It would replace my Polly policies
> and the correlation plumbing. Worth it at large scale; a lot of moving parts before that.

**"How do you handle stateful services?"**
> StatefulSets with persistent volumes — but I would rather use a managed database. Running
> PostgreSQL on Kubernetes is possible and is a specialist job; the compose file here does it
> for convenience, not as a recommendation.
