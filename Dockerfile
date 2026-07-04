# Build Stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app

# Copy everything and publish
COPY . .
RUN dotnet publish MoogleApi.slnx -c Release -o out

# Runtime Stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/out .

# Railway injects the PORT env variable automatically
EXPOSE 8080

ENTRYPOINT ["dotnet", "MoogleApi.dll"]