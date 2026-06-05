FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["shared/contracts/FbApi.Contracts.csproj", "shared/contracts/"]
COPY ["services/webhook-service/webhook-service.csproj", "services/webhook-service/"]
RUN dotnet restore "services/webhook-service/webhook-service.csproj"
COPY . .
WORKDIR "/src/services/webhook-service"
RUN dotnet publish "webhook-service.csproj" -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:3001
ENV ASPNETCORE_HTTP_PORTS=
EXPOSE 3001
ENTRYPOINT ["dotnet", "webhook-service.dll"]
