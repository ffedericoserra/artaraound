# Build context: the repository root (docker compose sets it), because the image
# also carries the frontends and the seed data the API serves.

# 1. The navigator's production build.
FROM node:22 AS navigator
WORKDIR /src/frontend-navigator
COPY frontend-navigator/package.json ./
RUN npm install
COPY frontend-navigator/ ./
# The navigator imports a few icons from the marketplace's assets.
COPY frontend/marketplace/assets/ /src/frontend/marketplace/assets/
RUN npm run build

# 2. The API. Restore first, on the project file alone, so the package layer is
#    cached until the dependencies change.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src
COPY backend-dotnet/ArtAround.Api/ArtAround.Api.csproj ArtAround.Api/
RUN dotnet restore ArtAround.Api/ArtAround.Api.csproj
COPY backend-dotnet/ArtAround.Api/ ArtAround.Api/
RUN dotnet publish ArtAround.Api/ArtAround.Api.csproj -c Release -o /out --no-restore

# 3. Runtime: the ASP.NET runtime only, no SDK.
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=api /out ./
COPY data/ /srv/artaround/data/
COPY uploads/ /srv/artaround/uploads/
COPY frontend/marketplace/ /srv/artaround/frontend/marketplace/
COPY --from=navigator /src/frontend-navigator/dist/ /srv/artaround/frontend-navigator/dist/
ENV Frontends__RepositoryRoot=/srv/artaround \
    Seed__DataDirectory=/srv/artaround/data \
    ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "ArtAround.Api.dll"]
