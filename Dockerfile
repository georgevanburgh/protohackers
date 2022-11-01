FROM mcr.microsoft.com/dotnet/sdk:6.0-alpine AS build
WORKDIR /source

# Copy csproj and restore as distinct layers
COPY *.sln .
COPY server/*.csproj ./server/
RUN dotnet restore

# copy everything else and build app
COPY server/. ./server/
WORKDIR /source/server
RUN dotnet publish -c release -o /out --no-restore

# final stage/image
FROM mcr.microsoft.com/dotnet/aspnet:6.0-alpine
WORKDIR /app
COPY --from=build /out ./
ENTRYPOINT ["dotnet", "server.dll"]