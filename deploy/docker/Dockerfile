# One Dockerfile for every service, parameterised by PROJECT.
#
# Nine near-identical Dockerfiles drift apart the moment one is changed and the
# others are not - and the one that drifts is the one that breaks at 2am. The
# build context is the repository root so the shared building blocks resolve.
#
#   docker build -f deploy/docker/Dockerfile \
#     --build-arg PROJECT=src/Services/StrategyOps.Projects.Api \
#     --build-arg ASSEMBLY=StrategyOps.Projects.Api \
#     -t strategyops/projects:local .

ARG DOTNET_VERSION=8.0

# --- build ------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
ARG PROJECT
ARG ASSEMBLY
WORKDIR /src

# Copy only what restore needs first, so the restore layer is cached and a code
# change does not re-download every package. This is the single biggest win in
# .NET image build times.
COPY Directory.Build.props Directory.Packages.props StrategyOps.sln ./
COPY src/BuildingBlocks/StrategyOps.Contracts/*.csproj src/BuildingBlocks/StrategyOps.Contracts/
COPY src/BuildingBlocks/StrategyOps.BuildingBlocks/*.csproj src/BuildingBlocks/StrategyOps.BuildingBlocks/
COPY ${PROJECT}/*.csproj ${PROJECT}/
RUN dotnet restore "${PROJECT}" /p:RestoreUseStaticGraphEvaluation=false

COPY src/ src/
RUN dotnet publish "${PROJECT}" -c Release -o /app/publish --no-restore /p:UseAppHost=false

# --- run --------------------------------------------------------------------
# The ASP.NET runtime image, not the SDK: roughly a fifth of the size, and it
# does not ship a compiler into production.
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS final
ARG ASSEMBLY
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    ASSEMBLY_NAME=${ASSEMBLY}

WORKDIR /app
COPY --from=build /app/publish .

# Non-root. A container that runs as root turns a code-execution bug into a
# host-level one, and it is a one-line change.
RUN adduser --disabled-password --gecos "" --uid 64198 appuser \
    && chown -R appuser:appuser /app
USER appuser

EXPOSE 8080

# Shell form so ASSEMBLY_NAME expands; exec so the app is PID 1 and receives
# SIGTERM directly, which is what makes graceful shutdown work.
ENTRYPOINT exec dotnet "${ASSEMBLY_NAME}.dll"
