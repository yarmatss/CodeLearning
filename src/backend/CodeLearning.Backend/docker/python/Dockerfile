FROM python:3.14.2-alpine

# Install required tools for test execution
# - time: GNU time with -v flag for memory measurement
# - coreutils: basic Unix utilities
# - jq: JSON processor for shell scripts
# - bc: calculator for floating-point math
# - bash: shell for running test scripts
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

# Security: Run as non-privileged user (will be set by Docker runner)
# Note: User will be set to nobody (65534:65534) by DockerRunner

# Metadata
LABEL org.opencontainers.image.title="CodeLearning Python Executor"
LABEL org.opencontainers.image.description="Python 3.14.2 with test execution tools"
LABEL org.opencontainers.image.version="3.14.2"
