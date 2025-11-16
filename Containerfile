FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine as buildlayer
WORKDIR /app
ARG BUILD=DEV

COPY . .
# Use the build ID as the app's version suffix
RUN dotnet publish \
    -c Release \
    -o published \
    -f net8.0 \
    --version-suffix $BUILD \
    src/FoxyBalance.Server/FoxyBalance.Server.fsproj

# Switch to alpine for running the application
FROM mcr.microsoft.com/dotnet/aspnet:8.0-jammy-chiseled-extra as runlayer
WORKDIR /app

# Copy the built files from both fsharp and node
COPY --from=0 /app/published ./published

# Change to /app/published so the program has the right ContentDirectoryRoot (i.e. it can load the App_Data files, etc.)
WORKDIR "/app/published"
EXPOSE 3000
ENTRYPOINT ["dotnet", "/app/published/foxy_balance.dll"]
