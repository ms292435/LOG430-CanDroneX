# Contexte de build : racine du dépôt (Directory.Build.props et Directory.Packages.props y sont partagés).
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props ./
COPY services/drone-management-service/src/DroneManagement.Domain/DroneManagement.Domain.csproj services/drone-management-service/src/DroneManagement.Domain/
COPY services/drone-management-service/src/DroneManagement.Application/DroneManagement.Application.csproj services/drone-management-service/src/DroneManagement.Application/
COPY services/drone-management-service/src/DroneManagement.Infrastructure/DroneManagement.Infrastructure.csproj services/drone-management-service/src/DroneManagement.Infrastructure/
COPY services/drone-management-service/src/DroneManagement.Api/DroneManagement.Api.csproj services/drone-management-service/src/DroneManagement.Api/
RUN dotnet restore services/drone-management-service/src/DroneManagement.Api/DroneManagement.Api.csproj
COPY services/drone-management-service/src/ services/drone-management-service/src/
RUN dotnet publish services/drone-management-service/src/DroneManagement.Api/DroneManagement.Api.csproj \
    -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "DroneManagement.Api.dll"]
