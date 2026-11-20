# One image recipe for every .NET service. Build context = repo root.
#   docker build -f infra/docker/service.Dockerfile --build-arg SERVICE=Network .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG SERVICE
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY libs/MetroRail.Common/MetroRail.Common.csproj libs/MetroRail.Common/
COPY services/${SERVICE}/MetroRail.${SERVICE}.csproj services/${SERVICE}/
RUN dotnet restore services/${SERVICE}/MetroRail.${SERVICE}.csproj
COPY libs/ libs/
COPY services/${SERVICE}/ services/${SERVICE}/
RUN dotnet publish services/${SERVICE}/MetroRail.${SERVICE}.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
ARG SERVICE
ENV SERVICE=${SERVICE} ASPNETCORE_HTTP_PORTS=8000 DOTNET_gcServer=1
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8000
ENTRYPOINT ["sh", "-c", "exec dotnet MetroRail.${SERVICE}.dll"]
