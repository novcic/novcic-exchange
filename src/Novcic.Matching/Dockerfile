FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY src/Novcic.Core/ src/Novcic.Core/
COPY src/Novcic.Matching/ src/Novcic.Matching/
RUN dotnet publish src/Novcic.Matching -c Release -o /app
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "Novcic.Matching.dll"]
