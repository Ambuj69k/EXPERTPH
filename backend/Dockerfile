# PharmaOS API — multi-stage build. Build context: ./backend
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
# nuget.config points the package cache at D:\ for local Windows dev; inside the container use the default Linux path
ENV NUGET_PACKAGES=/root/.nuget/packages
COPY nuget.config PharmaOS.sln ./
COPY src/PharmaOS.Core/PharmaOS.Core.csproj src/PharmaOS.Core/
COPY src/PharmaOS.Api/PharmaOS.Api.csproj src/PharmaOS.Api/
RUN dotnet restore src/PharmaOS.Api/PharmaOS.Api.csproj
COPY src/ src/
RUN dotnet publish src/PharmaOS.Api/PharmaOS.Api.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
# /data holds what must be backed up with the database: Data Protection keys (MFA secrets) and uploaded files
RUN mkdir -p /data/keys /data/files && chown -R $APP_UID /data
COPY --from=build /app .
USER $APP_UID
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    Security__KeysPath=/data/keys \
    Storage__Root=/data/files \
    ForwardedHeaders__Enabled=true
EXPOSE 8080
VOLUME ["/data"]
ENTRYPOINT ["dotnet", "PharmaOS.Api.dll"]
