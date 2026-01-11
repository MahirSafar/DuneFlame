# Multi-stage build for DuneFlame API
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

# Copy solution and project files
COPY ["DuneFlame.sln", "."]
COPY ["src/DuneFlame.Domain/DuneFlame.Domain.csproj", "src/DuneFlame.Domain/"]
COPY ["src/DuneFlame.Application/DuneFlame.Application.csproj", "src/DuneFlame.Application/"]
COPY ["src/DuneFlame.Infrastructure/DuneFlame.Infrastructure.csproj", "src/DuneFlame.Infrastructure/"]
COPY ["src/DuneFlame.API/DuneFlame.API.csproj", "src/DuneFlame.API/"]

# Restore dependencies
RUN dotnet restore "DuneFlame.sln"

# Copy remaining source code
COPY . .

# Build the application
RUN dotnet build "DuneFlame.sln" -c Release -o /app/build

# Publish the application
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

# Set environment variables
ENV ASPNETCORE_URLS=http://+:80
ENV ASPNETCORE_ENVIRONMENT=Production

# Health check
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
    CMD curl -f http://localhost/health || exit 1

# Run the application
ENTRYPOINT ["dotnet", "DuneFlame.API.dll"]
