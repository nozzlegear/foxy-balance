FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine@sha256:7d98d5883675c6bca25b1db91f393b24b85125b5b00b405e55404fd6b8d2aead as buildlayer
WORKDIR /app
ARG BUILD=DEV

COPY . .
# Use the build ID as the app's version suffix
RUN dotnet publish \
    -c Release \
    -o published \
    -f net10.0 \
    --version-suffix $BUILD \
    src/FoxyBalance.Server/FoxyBalance.Server.fsproj

# Switch to alpine for running the application
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra@sha256:64f42416803e32bee1f5d2d3eab5825581abd45b2e9c6f888fc873ff2c4cc378 as runlayer
WORKDIR /app

# Copy the built files from both fsharp and node
COPY --from=0 /app/published ./published

# Build args must be redeclared at each layer
ARG RUN=DEV
ARG COMMIT
# Connects the container to the Github repository. See https://docs.github.com/en/packages/learn-github-packages/connecting-a-repository-to-a-package#connecting-a-repository-to-a-container-image-using-the-command-line
LABEL org.opencontainers.image.source=https://github.com/nozzlegear/foxy-balance
LABEL org.opencontainers.image.revision=$COMMIT
LABEL foxybalance.build.id=$RUN

# Change to /app/published so the program has the right ContentDirectoryRoot (i.e. it can load the App_Data files, etc.)
WORKDIR "/app/published"
EXPOSE 3000
ENTRYPOINT ["dotnet", "/app/published/foxy_balance.dll"]
