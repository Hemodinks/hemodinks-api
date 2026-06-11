# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["HemodinksAPI.Api/HemodinksAPI.Api.csproj", "HemodinksAPI.Api/"]
COPY ["HemodinksAPI.Application/HemodinksAPI.Application.csproj", "HemodinksAPI.Application/"]
COPY ["HemodinksAPI.Domain/HemodinksAPI.Domain.csproj", "HemodinksAPI.Domain/"]
COPY ["HemodinksAPI.Infrastructure/HemodinksAPI.Infrastructure.csproj", "HemodinksAPI.Infrastructure/"]
RUN dotnet restore "HemodinksAPI.Api/HemodinksAPI.Api.csproj"

COPY . .
WORKDIR "/src/HemodinksAPI.Api"
RUN dotnet build "HemodinksAPI.Api.csproj" -c Release -o /app/build

# Stage 2: Publish
FROM build AS publish
RUN dotnet publish "HemodinksAPI.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Stage 3: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080

COPY --from=publish /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "HemodinksAPI.Api.dll"]
