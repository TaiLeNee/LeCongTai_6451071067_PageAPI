FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY shared/contracts/FbApi.Contracts.csproj shared/contracts/
RUN dotnet restore shared/contracts/FbApi.Contracts.csproj
COPY services/retry-service/retry-service.csproj services/retry-service/
RUN dotnet restore services/retry-service/retry-service.csproj
COPY shared/contracts/ shared/contracts/
COPY services/retry-service/ services/retry-service/
RUN dotnet build services/retry-service/retry-service.csproj -c Release -o /app/build
FROM build AS publish
RUN dotnet publish services/retry-service/retry-service.csproj -c Release -o /app/publish /p:UseAppHost=false
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=publish /app/publish .
EXPOSE 3003
ENV ASPNETCORE_URLS=http://+:3003
ENV ASPNETCORE_HTTP_PORTS=
ENTRYPOINT ["dotnet", "retry-service.dll"]
