# One image recipe for every .NET service. Build context = repo root.
#   docker build -f infra/docker/service.Dockerfile --build-arg SERVICE=Network .
# Shared build configuration for all .NET microservices.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

ARG SERVICE

WORKDIR /src

# Copy shared build and dependency configuration.
COPY global.json Directory.Build.props Directory.Packages.props ./

# Copy project files before restoring dependencies.
COPY libs/MetroRail.Common/MetroRail.Common.csproj libs/MetroRail.Common/
COPY services/${SERVICE}/MetroRail.${SERVICE}.csproj services/${SERVICE}/

# Restore NuGet dependencies.
RUN dotnet restore services/${SERVICE}/MetroRail.${SERVICE}.csproj

# Copy application source code and embedded resources.
COPY libs/ libs/
COPY services/${SERVICE}/ services/${SERVICE}/

# Compile and publish the selected microservice.
RUN dotnet publish services/${SERVICE}/MetroRail.${SERVICE}.csproj -c Release -o /app --no-restore

# Lightweight production runtime image.
FROM mcr.microsoft.com/dotnet/aspnet:10.0

ARG SERVICE

ENV SERVICE=${SERVICE} \
    ASPNETCORE_HTTP_PORTS=8000 \
    DOTNET_gcServer=1

WORKDIR /app

# Copy the published application.
COPY --from=build /app .

# Run the application as a non-root user.
USER $APP_UID

EXPOSE 8000

# Forward command-line arguments to the .NET application.
# This supports commands such as --import-timetable.
ENTRYPOINT ["sh", "-c", "exec dotnet MetroRail.${SERVICE}.dll \"$@\"", "--"]
