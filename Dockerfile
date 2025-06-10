# Stage 1: Build the application
# We use the .NET SDK image as the base for building. This image contains all
# necessary tools and runtimes to compile and publish a .NET application.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

# Set the working directory inside the container
WORKDIR /app

# Copy the project file (.csproj) first. This allows Docker to cache the
# dotnet restore command if only source code changes, speeding up builds.
COPY *.csproj ./
# Restore NuGet package dependencies.
RUN dotnet restore

# Copy the rest of the application's source code into the container.
COPY . .

# Publish the application.
# -c Release: Builds in Release configuration for optimization.
# -o out: Specifies the output directory as 'out'.
# --self-contained true: Makes the application self-contained, meaning it includes
#                       all necessary .NET runtime components and can run on a machine
#                       without a .NET runtime installed.
# -r linux-x64: Specifies the runtime identifier for Linux 64-bit systems.
# /p:PublishReadyToRun=true: Compiles application assemblies into a platform-specific
#                           format, improving startup time and reducing working set.
# /p:PublishTrimmed=true: Trims unused framework libraries, reducing the final bundle size.
# /p:UseAppHost=true: Creates a native executable for the application.
RUN dotnet publish -c Release -o out --self-contained true -r linux-x64 /p:PublishReadyToRun=true /p:PublishTrimmed=true /p:UseAppHost=true

# Stage 2: Create the final runtime image
# We use the 'runtime-deps' image, which is a minimal base image that contains
# only the operating system dependencies required for self-contained .NET applications.
# 'bookworm-slim' indicates a Debian Bookworm based slim image, known for its small size.
FROM mcr.microsoft.com/dotnet/runtime-deps:8.0-bookworm-slim AS final

# Set the working directory for the final image.
WORKDIR /app

# Copy the published application artifacts from the 'build' stage to the 'final' stage.
# This ensures that only the necessary executable and its dependencies are included,
# keeping the final image size minimal.
COPY --from=build /app/out .

# Ensure the executable has execute permissions. This is crucial for running it.
RUN chmod +x CSharpMemTest

# Set the entry point for the container. When the container starts, it will execute
# the 'CSharpMemTest' application.
ENTRYPOINT ["./CSharpMemTest"]
