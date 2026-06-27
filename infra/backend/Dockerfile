# Multi-stage build van de .NET-backend. Build-context = de repo-root
# (zie docker-compose.yml: context ".."), zodat backend/ beschikbaar is.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY backend/ ./backend/
RUN dotnet publish backend/src/ContactCenter.Api/ContactCenter.Api.csproj \
    -c Release -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app ./
# Binden op 0.0.0.0 zodat de (lokale) front-ends de backend op de VM bereiken.
ENV ASPNETCORE_URLS=http://0.0.0.0:5080
EXPOSE 5080
ENTRYPOINT ["dotnet", "ContactCenter.Api.dll"]
