# Multi-stage build van de .NET-backend. Build-context = de repo-root
# (zie docker-compose.yml: context ".."), zodat backend/ beschikbaar is.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY backend/ ./backend/
RUN dotnet publish backend/src/ContactCenter.Api/ContactCenter.Api.csproj \
    -c Release -o /app /p:UseAppHost=false

# Piper (lokale neural-TTS) + NL-stemmen ophalen in een aparte stage.
FROM debian:bookworm-slim AS piper
RUN apt-get update && apt-get install -y --no-install-recommends curl ca-certificates \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /opt/piper
# De tar bevat een top-level map piper/ (binary + libs + espeak-ng-data) → die strippen.
RUN curl -sSL https://github.com/rhasspy/piper/releases/download/2023.11.14-2/piper_linux_x86_64.tar.gz \
    | tar xz --strip-components=1
RUN mkdir -p voices && cd voices \
    && base=https://huggingface.co/rhasspy/piper-voices/resolve/main/nl/nl_NL \
    && curl -sSL -o nl_NL-pim-medium.onnx        "$base/pim/medium/nl_NL-pim-medium.onnx" \
    && curl -sSL -o nl_NL-pim-medium.onnx.json   "$base/pim/medium/nl_NL-pim-medium.onnx.json" \
    && curl -sSL -o nl_NL-ronnie-medium.onnx      "$base/ronnie/medium/nl_NL-ronnie-medium.onnx" \
    && curl -sSL -o nl_NL-ronnie-medium.onnx.json "$base/ronnie/medium/nl_NL-ronnie-medium.onnx.json"

FROM mcr.microsoft.com/dotnet/aspnet:10.0
# sox hersamplet de piper-output naar 8kHz mono; libgomp1 voor onnxruntime.
RUN apt-get update && apt-get install -y --no-install-recommends sox libgomp1 \
    && rm -rf /var/lib/apt/lists/*
COPY --from=piper /opt/piper /opt/piper
WORKDIR /app
COPY --from=build /app ./
# Binden op 0.0.0.0 zodat de (lokale) front-ends de backend op de VM bereiken.
ENV ASPNETCORE_URLS=http://0.0.0.0:5080
# Pad naar de piper-binary + stemmen (PiperTtsService roept piper hier aan).
ENV Piper__Dir=/opt/piper
EXPOSE 5080
ENTRYPOINT ["dotnet", "ContactCenter.Api.dll"]
