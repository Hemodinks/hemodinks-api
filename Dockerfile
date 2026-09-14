# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["nuget.config", "."]
COPY ["Directory.Build.props", "."]
COPY ["Directory.Packages.props", "."]
COPY ["HemodinksAPI.Api/HemodinksAPI.Api.csproj", "HemodinksAPI.Api/"]
COPY ["HemodinksAPI.Application/HemodinksAPI.Application.csproj", "HemodinksAPI.Application/"]
COPY ["HemodinksAPI.Domain/HemodinksAPI.Domain.csproj", "HemodinksAPI.Domain/"]
COPY ["HemodinksAPI.Infrastructure/HemodinksAPI.Infrastructure.csproj", "HemodinksAPI.Infrastructure/"]
COPY ["Hemodinks.ServiceDefaults/Hemodinks.ServiceDefaults.csproj", "Hemodinks.ServiceDefaults/"]
RUN dotnet restore "HemodinksAPI.Api/HemodinksAPI.Api.csproj"

COPY . .
WORKDIR "/src/HemodinksAPI.Api"

# Stage 2: Publish
FROM build AS publish
ARG TARGETARCH
ARG PUBLISH_READY_TO_RUN=true
RUN case "${TARGETARCH:-amd64}" in amd64) RID=linux-x64 ;; arm64) RID=linux-arm64 ;; *) exit 1 ;; esac \
    && dotnet publish "HemodinksAPI.Api.csproj" -c Release -r "$RID" --self-contained false \
       -o /app/publish /p:UseAppHost=false /p:PublishReadyToRun=$PUBLISH_READY_TO_RUN

# Stage 3: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080

COPY --from=publish /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:8080
# Configuration files are immutable in the container. Avoid consuming an
# inotify instance only to watch appsettings files that cannot change at runtime.
ENV DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE=false
ENV CORECLR_PROFILER={36032161-FFC0-4B61-B559-F6C5D41BAE5A}
ENV CORECLR_NEWRELIC_HOME=/app/newrelic
ENV CORECLR_PROFILER_PATH=/app/newrelic/libNewRelicProfiler.so
ENV NEWRELIC_LOG_DIRECTORY=/app/logs

ENTRYPOINT ["dotnet", "HemodinksAPI.Api.dll"]
