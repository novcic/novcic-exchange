FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY src/Novcic.Core/ src/Novcic.Core/
COPY src/Novcic.Api/ src/Novcic.Api/
RUN dotnet publish src/Novcic.Api -c Release -o /app
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "Novcic.Api.dll"]
