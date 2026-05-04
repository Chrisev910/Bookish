# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY FantasyBooks/FantasyBooks.csproj FantasyBooks/
RUN dotnet restore FantasyBooks/FantasyBooks.csproj
COPY FantasyBooks/ FantasyBooks/
WORKDIR /src/FantasyBooks
RUN dotnet publish -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "FantasyBooks.dll"]
