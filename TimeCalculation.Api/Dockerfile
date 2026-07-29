# Multi-stage build for TimeCalculation.Api (DEPLOY_PLAN.md §5 — the prerequisite for the api
# Terraform module, which builds an ECR image from this).
#
# Build from the repo root: `docker build -f TimeCalculation.Api/Dockerfile .`
# The context is the repo root (not this directory) because the project graph
# (Api -> Persistence, TimeCalculation -> Model) spans sibling directories that a
# TimeCalculation.Api-rooted context couldn't see.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy only the .csproj files first so `dotnet restore` is cached across builds that only change
# .cs source, not references — same reasoning CI already applies to `dotnet restore RobTime.sln`.
COPY TimeCalculation.Model/TimeCalculation.Model.csproj TimeCalculation.Model/
COPY TimeCalculation.Persistence/TimeCalculation.Persistence.csproj TimeCalculation.Persistence/
COPY TimeCalculation/TimeCalculation.csproj TimeCalculation/
COPY TimeCalculation.Api/TimeCalculation.Api.csproj TimeCalculation.Api/
RUN dotnet restore TimeCalculation.Api/TimeCalculation.Api.csproj

# Now the full source for just the projects TimeCalculation.Api actually depends on — not the whole
# repo (RobTimeUI, the test projects), which would bust the cache on every frontend/test change and
# has no bearing on this image.
COPY TimeCalculation.Model/ TimeCalculation.Model/
COPY TimeCalculation.Persistence/ TimeCalculation.Persistence/
COPY TimeCalculation/ TimeCalculation/
COPY TimeCalculation.Api/ TimeCalculation.Api/

RUN dotnet publish TimeCalculation.Api/TimeCalculation.Api.csproj \
    --no-restore \
    --configuration Release \
    --output /app

# The runtime image needs no SDK, no source — just the published output. aspnet (not the bare
# `dotnet` runtime image) because this is a Kestrel-hosted web app.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Non-root: the default `app` user ships in Microsoft's runtime images specifically for this.
USER app

COPY --from=build /app .

# App Runner's health check and the container's own listener both need a known port — matches
# ASPNETCORE_URLS below, not Kestrel's dev-only launchSettings.json ports (53533/53534), which are
# local-only and never read in a container.
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "TimeCalculation.Api.dll"]
