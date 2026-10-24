# Contexte de build : racine du dépôt (Directory.Build.props et Directory.Packages.props y sont partagés).
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props ./
COPY services/service-ordering-service/src/ServiceOrdering.Domain/ServiceOrdering.Domain.csproj services/service-ordering-service/src/ServiceOrdering.Domain/
COPY services/service-ordering-service/src/ServiceOrdering.Application/ServiceOrdering.Application.csproj services/service-ordering-service/src/ServiceOrdering.Application/
COPY services/service-ordering-service/src/ServiceOrdering.Infrastructure/ServiceOrdering.Infrastructure.csproj services/service-ordering-service/src/ServiceOrdering.Infrastructure/
COPY services/service-ordering-service/src/ServiceOrdering.Api/ServiceOrdering.Api.csproj services/service-ordering-service/src/ServiceOrdering.Api/
RUN dotnet restore services/service-ordering-service/src/ServiceOrdering.Api/ServiceOrdering.Api.csproj
COPY services/service-ordering-service/src/ services/service-ordering-service/src/
RUN dotnet publish services/service-ordering-service/src/ServiceOrdering.Api/ServiceOrdering.Api.csproj \
    -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "ServiceOrdering.Api.dll"]
