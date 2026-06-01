# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["HemodinksAPI.Api/HemodinksAPI.Api.csproj", "HemodinksAPI.Api/"]
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
EXPOSE 80
EXPOSE 443

COPY --from=publish /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:80;https://+:443

ENTRYPOINT ["dotnet", "HemodinksAPI.Api.dll"]
