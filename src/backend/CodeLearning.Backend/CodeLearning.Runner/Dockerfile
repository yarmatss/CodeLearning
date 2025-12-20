FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["CodeLearning.Runner/CodeLearning.Runner.csproj", "CodeLearning.Runner/"]
COPY ["CodeLearning.Core/CodeLearning.Core.csproj", "CodeLearning.Core/"]
COPY ["CodeLearning.Infrastructure/CodeLearning.Infrastructure.csproj", "CodeLearning.Infrastructure/"]
RUN dotnet restore "CodeLearning.Runner/CodeLearning.Runner.csproj"
COPY . .
WORKDIR "/src/CodeLearning.Runner"
RUN dotnet build "CodeLearning.Runner.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "CodeLearning.Runner.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
# NOTE: Running as non-root user for security
# Docker Socket Proxy handles Docker API access, so root is not needed
USER $APP_UID
WORKDIR /app
COPY --from=publish /app/publish .

# Create workspace directory with proper permissions
RUN mkdir -p /tmp/submissions && chmod 777 /tmp/submissions

ENTRYPOINT ["dotnet", "CodeLearning.Runner.dll"]
