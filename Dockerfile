# Multi-stage build for DuneFlame API
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

# Copy project files (only main projects, not tests)
COPY ["src/DuneFlame.Domain/DuneFlame.Domain.csproj", "src/DuneFlame.Domain/"]
COPY ["src/DuneFlame.Application/DuneFlame.Application.csproj", "src/DuneFlame.Application/"]
COPY ["src/DuneFlame.Infrastructure/DuneFlame.Infrastructure.csproj", "src/DuneFlame.Infrastructure/"]
COPY ["src/DuneFlame.API/DuneFlame.API.csproj", "src/DuneFlame.API/"]

# Restore dependencies for API project only
RUN dotnet restore "src/DuneFlame.API/DuneFlame.API.csproj"

# Copy all remaining source files
COPY . .

# Build the API project
RUN dotnet build "src/DuneFlame.API/DuneFlame.API.csproj" -c Release -o /app/build

# Publish the API project
FROM build AS publish
RUN dotnet publish "src/DuneFlame.API/DuneFlame.API.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Install curl for health checks
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*

COPY --from=publish /app/publish .

# Expose ports
EXPOSE 80
EXPOSE 443

# Environment variables
# Expose default Cloud Run port (read from PORT env variable)
ENV ASPNETCORE_URLS=http://+:${PORT:-8080}
EXPOSE ${PORT:-8080}
ENV ASPNETCORE_ENVIRONMENT=Production

# Health check
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
    CMD curl -f http://localhost/health || exit 1

ENTRYPOINT ["dotnet", "DuneFlame.API.dll"]
