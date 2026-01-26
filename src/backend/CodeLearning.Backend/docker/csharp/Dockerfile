FROM mcr.microsoft.com/dotnet/sdk:10.0 AS agent-build

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
       clang \
       zlib1g-dev \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /src
COPY ["CodeLearning.TestRunner/CodeLearning.TestRunner.csproj", "CodeLearning.TestRunner/"]
COPY ["CodeLearning.Core/CodeLearning.Core.csproj", "CodeLearning.Core/"]
RUN dotnet restore "CodeLearning.TestRunner/CodeLearning.TestRunner.csproj"
COPY . .
RUN dotnet publish "CodeLearning.TestRunner/CodeLearning.TestRunner.csproj" \
    -c Release -r linux-x64 --self-contained true \
    -p:PublishAot=true -p:InvariantGlobalization=true -p:StackTraceSupport=false -p:OptimizationPreference=Size \
    -o /out

FROM mcr.microsoft.com/dotnet/sdk:10.0

RUN apt-get update && apt-get install -y --no-install-recommends \
    time \
    jq \
    bash \
    && rm -rf /var/lib/apt/lists/*

ENV DOTNET_CLI_HOME=/tmp
ENV DOTNET_NOLOGO=true
ENV DOTNET_SKIP_FIRST_TIME_EXPERIENCE=true
ENV DOTNET_CLI_TELEMETRY_OPTOUT=true

WORKDIR /app


COPY --from=agent-build /out/CodeLearning.TestRunner /app/runner

RUN chmod +x /app/runner && chmod 777 /app

CMD ["tail", "-f", "/dev/null"]