FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY src/HelL.Contracts src/HelL.Contracts
COPY src/HelL.Remote src/HelL.Remote
COPY src/HelL.Relay src/HelL.Relay
RUN dotnet publish src/HelL.Relay -c Release -o /app --no-self-contained

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
RUN mkdir -p /state && chown "$APP_UID" /state
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "HelL.Relay.dll"]
