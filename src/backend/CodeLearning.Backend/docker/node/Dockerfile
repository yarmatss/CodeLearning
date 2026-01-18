FROM node:25.2.1-alpine

# Install required tools for test execution
RUN apk add --no-cache \
    time \
    coreutils \
    jq \
    bc \
    bash

# Create app directory
WORKDIR /app

# Fix permissions: allow nobody user (65534) to write in /app
RUN chmod 777 /app

# Metadata
LABEL org.opencontainers.image.title="CodeLearning Node.js Executor"
LABEL org.opencontainers.image.description="Node.js 25.2.1 with test execution tools"
LABEL org.opencontainers.image.version="25.2.1"
