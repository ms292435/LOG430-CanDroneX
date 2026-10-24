# Contexte de build : racine du dépôt (Directory.Build.props et Directory.Packages.props y sont partagés).
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props ./
COPY services/slice-service/src/SliceService.Domain/SliceService.Domain.csproj services/slice-service/src/SliceService.Domain/
COPY services/slice-service/src/SliceService.Application/SliceService.Application.csproj services/slice-service/src/SliceService.Application/
COPY services/slice-service/src/SliceService.Infrastructure/SliceService.Infrastructure.csproj services/slice-service/src/SliceService.Infrastructure/
COPY services/slice-service/src/SliceService.Api/SliceService.Api.csproj services/slice-service/src/SliceService.Api/
RUN dotnet restore services/slice-service/src/SliceService.Api/SliceService.Api.csproj
COPY services/slice-service/src/ services/slice-service/src/
RUN dotnet publish services/slice-service/src/SliceService.Api/SliceService.Api.csproj \
    -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "SliceService.Api.dll"]
