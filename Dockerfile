FROM mcr.microsoft.com/dotnet/sdk:6.0-focal as buildlayer
WORKDIR /app

# Restore dotnet packages
COPY ./.config ./.config
COPY paket.lock paket.dependencies ./
RUN dotnet tool restore
RUN dotnet paket restore

# Get the build ID from --build-arg BUILD=xyz
ARG BUILD=DEV

# Copy everything and build for musl (alpine) systems
COPY . .
# Use the build ID as the app's version suffix
RUN dotnet publish \
    -c Release \
    -o published \
    -r linux-musl-x64 \
    --version-suffix $BUILD \
    --self-contained true \
    src/FoxyBalance.Server

# Switch to alpine for running the application
FROM mcr.microsoft.com/dotnet/aspnet:6.0-alpine as runlayer
WORKDIR /app

# Add timezone database for timezone lookups
# https://www.stevejgordon.co.uk/timezonenotfoundexception-in-alpine-based-docker-images
RUN apk add tzdata
# Fix SqlClient invariant errors when dotnet core runs in an alpine container
# https://github.com/dotnet/SqlClient/issues/220
RUN apk add icu-libs
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

# Copy the built files from both fsharp and node
COPY --from=0 /app/published ./published
RUN du -sh -- *

# Change to /app/published so the program has the right ContentDirectoryRoot (i.e. it can load the App_Data files, etc.)
WORKDIR "/app/published"
EXPOSE 3000
CMD ["./foxy_balance"]
