FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081


FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["LexisNexis.DocumentIntake.Api/LexisNexis.DocumentIntake.Api.csproj", "LexisNexis.DocumentIntake.Api/"]
COPY ["LexisNexis.DocumentIntake.BusinessLogic/LexisNexis.DocumentIntake.BusinessLogic.csproj", "LexisNexis.DocumentIntake.BusinessLogic/"]
COPY ["LexisNexis.DocumentIntake.Infrastructure/LexisNexis.DocumentIntake.Infrastructure.csproj", "LexisNexis.DocumentIntake.Infrastructure/"]
COPY ["LexisNexis.DocumentIntake.Reporting/LexisNexis.DocumentIntake.Reporting.csproj", "LexisNexis.DocumentIntake.Reporting/"]
RUN dotnet restore "./LexisNexis.DocumentIntake.Api/LexisNexis.DocumentIntake.Api.csproj"
COPY . .
WORKDIR "/src/LexisNexis.DocumentIntake.Api"
RUN dotnet build "./LexisNexis.DocumentIntake.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./LexisNexis.DocumentIntake.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "LexisNexis.DocumentIntake.Api.dll"]
