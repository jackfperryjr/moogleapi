# Build Stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app

# Copy the entire repository layout
COPY . .

# Target the specific web project file inside your src directory for direct output flattening
RUN dotnet publish src/MoogleAPI.Web/MoogleAPI.Web.csproj -c Release -o out

# Runtime Stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/out .

# Handle the dynamic port mapping via an env fallback inside the container
ENV ASPNETCORE_HTTP_PORTS=8080

ENTRYPOINT ["dotnet", "MoogleAPI.Web.dll"]