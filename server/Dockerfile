FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app
COPY . .
RUN dotnet publish -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /out .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
# The release version, baked in by release.yml (logged as Version). Unset for local builds, where
# Program.cs falls back to the assembly version.
ARG APP_VERSION
ENV APP_VERSION=${APP_VERSION}
ENTRYPOINT ["dotnet", "DotWatcher.Server.dll"]
