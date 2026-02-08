FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/DuneFlame.Domain/DuneFlame.Domain.csproj", "src/DuneFlame.Domain/"]
COPY ["src/DuneFlame.Application/DuneFlame.Application.csproj", "src/DuneFlame.Application/"]
COPY ["src/DuneFlame.Infrastructure/DuneFlame.Infrastructure.csproj", "src/DuneFlame.Infrastructure/"]
COPY ["src/DuneFlame.API/DuneFlame.API.csproj", "src/DuneFlame.API/"]

RUN dotnet restore "src/DuneFlame.API/DuneFlame.API.csproj"

COPY . .

RUN dotnet publish "src/DuneFlame.API/DuneFlame.API.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080
ENV PORT=8080
EXPOSE 8080

COPY --from=build /app/publish .

RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*
HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "DuneFlame.API.dll"]