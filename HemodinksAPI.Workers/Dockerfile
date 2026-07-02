# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["HemodinksAPI.Application/HemodinksAPI.Application.csproj", "HemodinksAPI.Application/"]
COPY ["HemodinksAPI.Domain/HemodinksAPI.Domain.csproj", "HemodinksAPI.Domain/"]
COPY ["HemodinksAPI.Infrastructure/HemodinksAPI.Infrastructure.csproj", "HemodinksAPI.Infrastructure/"]
COPY ["HemodinksAPI.Workers/HemodinksAPI.Workers.csproj", "HemodinksAPI.Workers/"]
RUN dotnet restore "HemodinksAPI.Workers/HemodinksAPI.Workers.csproj"

COPY . .
WORKDIR "/src/HemodinksAPI.Workers"
RUN dotnet publish "HemodinksAPI.Workers.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Stage 2: Azure Functions runtime
FROM mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated10.0 AS final
WORKDIR /home/site/wwwroot

COPY --from=build /app/publish .

ENV AzureWebJobsScriptRoot=/home/site/wwwroot
ENV FUNCTIONS_WORKER_RUNTIME=dotnet-isolated
ENV AzureFunctionsJobHost__Logging__Console__IsEnabled=true
