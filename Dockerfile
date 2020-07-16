FROM mcr.microsoft.com/dotnet/core/sdk:3.1
WORKDIR /app

# Add paket global tool for restoring packages
RUN dotnet tool install -g paket
ENV PATH="/root/.dotnet/tools:${PATH}"

# Restore dotnet packages
COPY paket.lock paket.dependencies ./
RUN paket restore

# Get the build ID from --build-arg BUILD=xyz
ARG BUILD=DEV

# Copy everything and build for musl (alpine) systems
COPY . .
# Use the build ID as the app's version suffix
RUN dotnet publish -c Release -o published -r linux-musl-x64 --version-suffix $BUILD

# Switch to alpine for running the application
FROM mcr.microsoft.com/dotnet/core/runtime:3.1-alpine
WORKDIR /app

# Fix SqlClient invariant errors when dotnet core runs in an alpine container
# https://github.com/dotnet/SqlClient/issues/220
RUN apk add icu-libs
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

# Copy the built files from both fsharp and node
COPY --from=0 /app/published ./published
RUN du -sh -- *

# Change to /app/published so the program has the right ContentDirectoryRoot (i.e. it can load the App_Data files, appsettings.json files, etc.)
WORKDIR "/app/published"
EXPOSE 3000
CMD ["dotnet", "./foxy_balance.dll"]
