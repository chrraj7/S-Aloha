FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY *.csproj .
RUN dotnet restore
COPY . .
# ADM-21 : version et commit affichés par Administration › Santé (fournis par la publication).
ARG VERSION=0.0.0-dev
ARG COMMIT=
RUN dotnet publish -c Release -o /app -p:Version=${VERSION} -p:SourceRevisionId=${COMMIT}

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
# L'image aspnet:10.0 écoute déjà sur 8080 (HTTP_PORTS) : pas d'ASPNETCORE_URLS, qui ferait
# journaliser un avertissement de surcharge au démarrage. 8081 : port interne des fonctionnalités
# optionnelles (OPT-11), à ne jamais publier ; seules les routes /internal y répondent.
ENV ASPNETCORE_HTTP_PORTS=8080;8081
EXPOSE 8080 8081
ENTRYPOINT ["dotnet", "SAloha.Api.dll"]
