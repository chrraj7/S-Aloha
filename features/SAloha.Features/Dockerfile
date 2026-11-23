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
# Port 8080 (HTTP_PORTS de l'image aspnet), sur le réseau du compose seulement : ni publié, ni
# relayé par nginx ; seule l'API l'appelle, avec le jeton du conteneur (OPT-10). Aucun état à
# écrire : l'utilisateur sans privilège de l'image suffit.
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "SAloha.Features.dll"]
