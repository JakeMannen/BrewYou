# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

# Copy project files and restore dependencies
COPY ["src/BrewYou.ApiService/BrewYou.ApiService.csproj", "src/BrewYou.ApiService/"]
COPY ["src/BrewYou.ServiceDefaults/BrewYou.ServiceDefaults.csproj", "src/BrewYou.ServiceDefaults/"]
RUN dotnet restore "src/BrewYou.ApiService/BrewYou.ApiService.csproj"

# Copy source files and publish
COPY ["src/BrewYou.ApiService/", "src/BrewYou.ApiService/"]
COPY ["src/BrewYou.ServiceDefaults/", "src/BrewYou.ServiceDefaults/"]
WORKDIR "/src/src/BrewYou.ApiService"
RUN dotnet publish "BrewYou.ApiService.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
WORKDIR /app

# Install ICU and timezone data for multi-language localization support (en-US, sv-SE, etc.)
RUN apk add --no-cache icu-libs icu-data-full tzdata

EXPOSE 5000
ENV ASPNETCORE_HTTP_PORTS=5000 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    DOTNET_EnableDiagnostics=0

# Run as non-root user
USER $APP_UID

COPY --from=build --chown=$APP_UID:$APP_UID /app/publish .

ENTRYPOINT ["dotnet", "BrewYou.ApiService.dll"]
