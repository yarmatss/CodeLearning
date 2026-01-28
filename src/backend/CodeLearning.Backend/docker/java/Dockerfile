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


FROM eclipse-temurin:21-jdk-jammy

RUN apt-get update && apt-get install -y --no-install-recommends \
    bash \
    time \
    jq \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

COPY --from=agent-build /out/CodeLearning.TestRunner /app/runner

RUN chmod +x /app/runner && chmod 777 /app

CMD ["tail", "-f", "/dev/null"]