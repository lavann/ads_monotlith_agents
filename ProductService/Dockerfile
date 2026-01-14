# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["ProductService.csproj", "./"]
RUN dotnet restore
COPY . .
RUN dotnet build "ProductService.csproj" -c Release -o /app/build
RUN dotnet publish "ProductService.csproj" -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "ProductService.dll"]
