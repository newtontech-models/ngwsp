# build the backend
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend-build-env

ARG CACHE_BUST
ARG PACKAGE_REGISTRY_USERNAME=""

WORKDIR /app
COPY ngwsp ngwsp
COPY proto proto

WORKDIR /app/ngwsp

# layer is not rebuilt on secret change without a CACHE_BUST manual intervention
RUN \
      dotnet publish -c Release -r linux-x64 --self-contained true --property AssemblyVersion=1.0.0.0 /p:PublishSingleFile=true -o /app/build

# build runner image
FROM ubuntu:24.04

ARG COMMIT_HASH="undefined"

LABEL org.opencontainers.image.source=https://github.com/newtontech-models/ngwsp
LABEL newtontechnologies.commit.hash="${COMMIT_HASH}"

RUN apt-get update && \
    apt-get install -y --no-install-recommends \
        ca-certificates libicu-dev tzdata && \
    rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=backend-build-env /app/build/ ./

EXPOSE 2380
ENV NGWSP_LISTEN_URL=http://localhost:2380
CMD ["./ngwsp", "proxy"]

# Build from the repository root:
# export PACKAGE_REGISTRY_TOKEN="***"
# docker buildx build -t ngwsp -o type=docker \
#   --build-arg CACHE_BUST="$(echo "$PACKAGE_REGISTRY_TOKEN" | sha256sum)" \
#   -f Dockerfile --load .

# To run standalone, remember to provide settings overrides and setup bidirectional networking:
#   - docker run --rm -p 2380:2380 ngwsp
