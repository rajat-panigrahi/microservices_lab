# 21. How do you deploy microservices?

*Asked by: Infosys, Capgemini, Virtusa, Cognizant*

## The 60-second answer

> Each service builds into its own container image, tagged with the commit, and deploys
> independently. That independence is the whole point — if two services must ship together,
> they are one service.
>
> A pipeline per service: build → test → build image → push to a registry → deploy to staging →
> deploy to production. Config comes from environment variables and secrets, never baked into
> the image, so the same image runs in every environment.
>
> The parts that are specific to microservices are **database migrations** and **contract
> compatibility**. You cannot deploy nine services atomically, so at some moment version N and
> N+1 are running simultaneously — every change has to be backwards compatible for at least one
> release.

## The rules that make it work

**One image, all environments.** Config from environment variables. If you build a separate
image per environment, you are testing something you will not ship.

**Backwards compatibility for one release, always.** Nine services cannot deploy atomically.
Concretely: add a column before you write to it, add a field to an event before you require it,
and never rename or remove in the same release as introducing the replacement. My contract
snapshot tests exist to make that a conscious decision rather than an accident.

**Migrations are the risky part.** Expand/contract:

1. **Expand** — add the new column, nullable. Deploy. Old code ignores it.
2. **Migrate** — new code writes both old and new. Backfill.
3. **Contract** — once nothing reads the old column, drop it. A *separate* release.

Three releases to rename a column, and that is the price of not having downtime.

**Deployment strategies:**

| Strategy | How | Cost |
|---|---|---|
| Rolling | replace pods gradually | both versions live at once |
| Blue/green | two environments, switch traffic | double the infrastructure |
| Canary | 5% of traffic first, then ramp | needs good metrics to judge |

Rolling is the Kubernetes default and what the manifests here use, with `maxUnavailable: 0` so
new pods must become **ready** before old ones are removed.

## In this repo

- One parameterised [`Dockerfile`](../../deploy/docker/Dockerfile) for all nine services, and
  [`build-images.sh`](../../deploy/build-images.sh). Nine near-identical Dockerfiles drift apart
  the moment one is changed, and the one that drifted is the one that breaks at 2am.
- [`docker-compose.yml`](../../deploy/docker-compose.yml) with PostgreSQL, RabbitMQ, Seq, Jaeger
- [`deploy/k8s/strategyops.yaml`](../../deploy/k8s/strategyops.yaml)
- [`deploy/local/run-all.sh`](../../deploy/local/run-all.sh) for running without containers

**Honest caveat:** none of the container artifacts have been executed — this environment has no
Docker daemon and no cluster. Both YAML files parse and were reviewed by eye. Everything else
in this repo was verified running.

## Migrations on startup — and why it does not scale

Each service calls `Database.MigrateAsync()` at boot. Simple, and fine for a lab. In production
it breaks: two replicas starting simultaneously both try to migrate, and a failed migration
crash-loops the pod. The usual fix is a **Kubernetes Job or init container** that runs
migrations once before the deployment rolls, so the application only ever reads a schema that
is already correct. Being able to name that as a known limitation is better than not having
thought about it.

## Follow-up probes

**"How do you handle configuration?"**
> Environment variables for most of it, a secret store for credentials. In Kubernetes:
> ConfigMap plus Secret, mounted as env vars — which is what the manifests here do. Never
> `appsettings.Production.json` in the image, because then a config change needs a rebuild.

**"How do you roll back?"**
> Redeploy the previous image tag — fast and safe for code. **Data is the hard part:** if the
> new version wrote a new column, rolling back the code does not roll back the data. That is
> the real reason for expand/contract — each step is independently reversible.

**"What about the database per service?"**
> Each service owns its schema and its migrations, and nobody else touches them. In compose I
> run one PostgreSQL server with seven separate databases: a shared *server* is a cost
> decision, a shared *database* is an architecture decision and a bad one.

**"CI/CD tooling?"**
> Any of GitHub Actions, Azure DevOps or GitLab CI. What matters more than the tool is the
> shape: one pipeline per service, triggered by changes to that service's path, so a change to
> Risk does not rebuild and redeploy the other eight.
