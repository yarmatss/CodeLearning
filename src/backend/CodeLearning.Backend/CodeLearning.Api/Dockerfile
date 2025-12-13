# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base

# Install required system libraries for PostgreSQL GSSAPI support
RUN apt-get update && \
    apt-get install -y --no-install-recommends \
        libgssapi-krb5-2 \
        curl && \
    apt-get clean && \
    rm -rf /var/lib/apt/lists/*

# Create directory for Data Protection keys
RUN mkdir -p /app/keys && chmod 777 /app/keys

USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081


# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["CodeLearning.Api/CodeLearning.Api.csproj", "CodeLearning.Api/"]
COPY ["CodeLearning.Application/CodeLearning.Application.csproj", "CodeLearning.Application/"]
COPY ["CodeLearning.Core/CodeLearning.Core.csproj", "CodeLearning.Core/"]
COPY ["CodeLearning.Infrastructure/CodeLearning.Infrastructure.csproj", "CodeLearning.Infrastructure/"]
RUN dotnet restore "./CodeLearning.Api/CodeLearning.Api.csproj"
COPY . .
WORKDIR "/src/CodeLearning.Api"
RUN dotnet build "./CodeLearning.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./CodeLearning.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "CodeLearning.Api.dll"]