FROM mcr.microsoft.com/dotnet/sdk:10.0.404-alpine3.24@sha256:0294f285d062afacc8b5f6d7febb0cd4ee879320e5585fa15f2f4ebf1feb9ba7 as build-layer
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

# "Chisel" the wget binary from the Ubuntu image so we can use it in the container's healthcheck
FROM docker.io/library/ubuntu:26.04@sha256:d66b366dac61b575bee2aef07e9c26e8070096fbe84a3d4d3eb0da069b5d76db AS wget-slice

RUN apt-get update && apt-get install -y wget

# Install and verify the chisel binary
ARG TARGETARCH
RUN wget "https://github.com/canonical/chisel/releases/download/v1.4.1/chisel_v1.4.1_linux_${TARGETARCH}.tar.gz" && \
    wget "https://github.com/canonical/chisel/releases/download/v1.4.1/chisel_v1.4.1_linux_${TARGETARCH}.tar.gz.sha384" && \
    sha384sum -c "chisel_v1.4.1_linux_${TARGETARCH}.tar.gz.sha384" && \
    tar zxvf "chisel_v1.4.1_linux_${TARGETARCH}.tar.gz" && \
    mv chisel /usr/local/bin/

# Cut the wget slice with chisel
RUN mkdir -p /wget-slice && chisel cut --release ubuntu-26.04 --root /wget-slice wget_bins

# Switch to the chiseled runtime image (Ubuntu 26.04) for running the application
FROM mcr.microsoft.com/dotnet/aspnet:10.0.15-resolute-chiseled-extra@sha256:728d90568c65ec25adf8bacd9da65356a7f5bbeb37eaca1193e6ed082900a1bc as runlayer
WORKDIR /app

# Copy the published files from the build layer
COPY --from=build-layer /app/published ./published
# Copy the wget binary from the wget-slice layer
COPY --from=wget-slice /wget-slice /

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
