FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Sses.Api/Sses.Api.csproj Sses.Api/
RUN dotnet restore Sses.Api/Sses.Api.csproj
COPY Sses.Api/ Sses.Api/
RUN dotnet publish Sses.Api/Sses.Api.csproj -c Release -o /out --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out .
ENV ASPNETCORE_URLS=http://+:5080
EXPOSE 5080
ENTRYPOINT ["dotnet", "Sses.Api.dll"]
