FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Directory.Packages.props", "."]
COPY ["CodeLearning.DockerGuard/CodeLearning.DockerGuard.csproj", "CodeLearning.DockerGuard/"]
RUN dotnet restore "CodeLearning.DockerGuard/CodeLearning.DockerGuard.csproj"
COPY CodeLearning.DockerGuard/ CodeLearning.DockerGuard/
RUN dotnet publish "CodeLearning.DockerGuard/CodeLearning.DockerGuard.csproj" -c "$BUILD_CONFIGURATION" -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
# The Docker API's conventional plain-HTTP port, in place of the image's default 8080
ENV ASPNETCORE_HTTP_PORTS=2375
# Runs as root only to open the Docker socket; compose drops every capability and makes the filesystem read-only
ENTRYPOINT ["dotnet", "CodeLearning.DockerGuard.dll"]
