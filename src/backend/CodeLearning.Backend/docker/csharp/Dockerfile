# Base images are pinned by digest (multi-arch index), so a rebuild cannot silently pick up a different image.
# To update: docker pull <image:tag>, then copy the digest from `docker image inspect --format '{{json .RepoDigests}}'`

# ---- Agent (CodeLearning.TestRunner), compiled ahead of time for the platform being built ----
FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:212fe4cb74627d5adf9817a4088906ae3edff1bd853bb27565d4b46731efe61e AS agent-build
ARG TARGETARCH

RUN apt-get update \
    && apt-get install -y --no-install-recommends clang zlib1g-dev \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /src
COPY ["Directory.Packages.props", "."]
COPY ["CodeLearning.TestRunner/CodeLearning.TestRunner.csproj", "CodeLearning.TestRunner/"]
COPY ["CodeLearning.Core/CodeLearning.Core.csproj", "CodeLearning.Core/"]
RUN case "${TARGETARCH:-amd64}" in \
        amd64) echo linux-x64 > /tmp/rid ;; \
        arm64) echo linux-arm64 > /tmp/rid ;; \
        *) echo "Unsupported TARGETARCH ${TARGETARCH}" >&2; exit 1 ;; \
    esac \
    && dotnet restore "CodeLearning.TestRunner/CodeLearning.TestRunner.csproj" -r "$(cat /tmp/rid)"
# Only the projects the agent is built from, so unrelated changes don't invalidate this layer
COPY ["CodeLearning.TestRunner/", "CodeLearning.TestRunner/"]
COPY ["CodeLearning.Core/", "CodeLearning.Core/"]
RUN dotnet publish "CodeLearning.TestRunner/CodeLearning.TestRunner.csproj" \
    -c Release -r "$(cat /tmp/rid)" --self-contained true --no-restore \
    -p:PublishAot=true -p:InvariantGlobalization=true -p:StackTraceSupport=false -p:OptimizationPreference=Size \
    -o /out

# ---- Executor ----
FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:212fe4cb74627d5adf9817a4088906ae3edff1bd853bb27565d4b46731efe61e

# time: per-test statistics (/usr/bin/time -v). util-linux: setpriv, which drops the submission to uid 65534.
# jq: builds the compile-error JSON
RUN apt-get update && apt-get install -y --no-install-recommends \
    bash \
    jq \
    time \
    util-linux \
    && rm -rf /var/lib/apt/lists/* \
    && test -x /usr/bin/time && command -v setpriv && command -v timeout

# The submission runs as uid 65534 with a read-only root filesystem: everything the SDK writes goes to /tmp.
# No build servers or node reuse: they would outlive the compile step inside the pids/memory limits
ENV DOTNET_CLI_HOME=/tmp \
    HOME=/tmp \
    DOTNET_NOLOGO=true \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=true \
    DOTNET_CLI_TELEMETRY_OPTOUT=true \
    DOTNET_GENERATE_ASPNET_CERTIFICATE=false \
    DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE=true \
    DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1 \
    MSBUILDDISABLENODEREUSE=1 \
    DOTNET_EnableDiagnostics=0 \
    NUGET_PACKAGES=/opt/codelearning/nuget

# Prebuilt project template. It is restored and built here at /sandbox, the path the submission is built at
# (a tmpfs at run time), so the generated obj/ files are valid there and the build needs neither network
# nor a restore. The generated script copies it into /sandbox and replaces Program.cs with the submission
RUN mkdir -p /sandbox /opt/codelearning \
    && cd /sandbox \
    && dotnet new console -n Solution -o . \
    && dotnet build Solution.csproj -c Release -p:UseSharedCompilation=false -nodeReuse:false -m:1 \
    && dotnet build-server shutdown \
    && rm -rf bin \
    && mv /sandbox /opt/codelearning/csharp-template \
    && mkdir /sandbox \
    && chmod -R a+rX /opt/codelearning \
    && rm -rf /tmp/* /tmp/.[!.]*

# The agent and everything it writes (/app/out, a tmpfs at run time) are root-only: the submission runs as
# uid 65534 and can neither read the test inputs nor forge the result. /sandbox is the submission's tmpfs
COPY --from=agent-build /out/CodeLearning.TestRunner /app/runner
RUN chown -R 0:0 /app && chmod 0700 /app /app/runner && mkdir -p /app/out

# DockerRunner always sets the command and user; these only make a manual `docker run` harmless
USER 0:0
CMD ["/bin/false"]
