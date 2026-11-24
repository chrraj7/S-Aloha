FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY *.csproj .
RUN dotnet restore
COPY . .
# ADM-21 : version rendue par le contrôle de santé (fournie par la publication).
ARG VERSION=0.0.0-dev
ARG COMMIT=
RUN dotnet publish -c Release -o /app -p:Version=${VERSION} -p:SourceRevisionId=${COMMIT}

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
# Port 8080 (HTTP_PORTS de l'image aspnet), sur le réseau du compose seulement : nginx y relaie
# /mcp, l'API y lit /health. Ni base, ni secret, ni état à écrire (MCP-02) : l'utilisateur sans
# privilège de l'image suffit.
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "SAloha.Mcp.dll"]
