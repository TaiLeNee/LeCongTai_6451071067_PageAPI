FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["shared/contracts/FbApi.Contracts.csproj", "shared/contracts/"]
COPY ["services/core-service/core-service.csproj", "services/core-service/"]
RUN dotnet restore "services/core-service/core-service.csproj"
COPY . .
WORKDIR "/src/services/core-service"
RUN dotnet publish "core-service.csproj" -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENV Gemini__ApiKey=""
ENV ASPNETCORE_URLS=http://+:3002
ENV ASPNETCORE_HTTP_PORTS=
EXPOSE 3002
ENTRYPOINT ["dotnet", "core-service.dll"]
