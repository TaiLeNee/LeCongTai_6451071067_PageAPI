FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["shared/contracts/FbApi.Contracts.csproj", "shared/contracts/"]
COPY ["services/backend-api/backend-api.csproj", "services/backend-api/"]
RUN dotnet restore "services/backend-api/backend-api.csproj"
COPY . .
WORKDIR "/src/services/backend-api"
RUN dotnet publish "backend-api.csproj" -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 3000
ENV ASPNETCORE_URLS=http://+:3000
ENV ASPNETCORE_HTTP_PORTS=
ENV ConnectionStrings__DefaultConnection=""
ENTRYPOINT ["dotnet", "backend-api.dll"]
