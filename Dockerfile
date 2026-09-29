# ── Build stage ───────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY src/JeemzuApi/JeemzuApi.csproj src/JeemzuApi/
RUN dotnet restore src/JeemzuApi/JeemzuApi.csproj

COPY src/ src/
RUN dotnet publish src/JeemzuApi/JeemzuApi.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

# ── Runtime stage ─────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

COPY --from=build /app/publish .

EXPOSE 8080

# Render injects PORT; the default keeps docker compose and plain docker run working.
ENTRYPOINT ["sh", "-c", "ASPNETCORE_URLS=http://+:${PORT:-8080} dotnet JeemzuApi.dll"]
