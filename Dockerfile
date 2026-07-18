# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY FantasyBooks/FantasyBooks.csproj FantasyBooks/
RUN dotnet restore FantasyBooks/FantasyBooks.csproj
COPY FantasyBooks/ FantasyBooks/
WORKDIR /src/FantasyBooks
RUN dotnet publish -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
# Nelknet.LibSQL sometimes nests native libs; ensure libsql.so is on the loader path for linux-x64.
RUN if [ -f /app/runtimes/linux-x64/native/linux-x64/native/libsql.so ]; then \
      mkdir -p /app/runtimes/linux-x64/native && \
      cp /app/runtimes/linux-x64/native/linux-x64/native/libsql.so /app/runtimes/linux-x64/native/libsql.so; \
    fi
# Default aspnet image runs the app as non-root; /app is root-owned after COPY,
# so SQLite cannot create library.db without write access.
RUN chown -R $APP_UID:$APP_UID /app
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "FantasyBooks.dll"]
