FROM mcr.microsoft.com/dotnet/sdk:10.0

# Install required tools for test execution
# - time: GNU time with -v flag for memory measurement
# - coreutils: basic Unix utilities
# - jq: JSON processor for shell scripts
# - bc: calculator for floating-point math
# Note: bash is already included in this image
RUN apt-get update && apt-get install -y \
    time \
    coreutils \
    jq \
    bc \
    && rm -rf /var/lib/apt/lists/*

# Create app directory
WORKDIR /app

# Fix permissions: allow nobody user (65534) to write in /app
RUN chmod 777 /app

# Set environment variables for .NET CLI
# - DOTNET_CLI_HOME: Home directory for .NET CLI cache/temp files
# - DOTNET_SKIP_FIRST_TIME_EXPERIENCE: Skip first-run experience
# - DOTNET_NOLOGO: Suppress .NET logo
ENV DOTNET_CLI_HOME=/tmp \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
    DOTNET_NOLOGO=1

# Security: Run as non-privileged user (will be set by Docker runner)
# Note: User will be set to nobody (65534:65534) by DockerRunner

# Metadata
LABEL org.opencontainers.image.title="CodeLearning C# Executor"
LABEL org.opencontainers.image.description=".NET 10.0 SDK with test execution tools"
LABEL org.opencontainers.image.version="10.0"
