FROM eclipse-temurin:21-jdk-jammy

# Install required tools for test execution
# - time: GNU time with -v flag for memory measurement
# - coreutils: basic Unix utilities
# - jq: JSON processor for shell scripts
# - bc: calculator for floating-point math
# - bash: shell for running test scripts
RUN apt-get update && apt-get install -y \
    time \
    coreutils \
    jq \
    bc \
    bash \
    && rm -rf /var/lib/apt/lists/*

# Create app directory
WORKDIR /app

# Fix permissions: allow nobody user (65534) to write in /app
RUN chmod 777 /app

# Security: Run as non-privileged user (will be set by Docker runner)
# Note: User will be set to nobody (65534:65534) by DockerRunner

# Metadata
LABEL org.opencontainers.image.title="CodeLearning Java Executor"
LABEL org.opencontainers.image.description="Eclipse Temurin JDK 21 with test execution tools"
LABEL org.opencontainers.image.version="21"
