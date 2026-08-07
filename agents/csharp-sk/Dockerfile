# Sandbox image for the C# / Semantic Kernel agent.
# Includes the .NET 8 SDK + dotnet-script so the agent can author and execute
# C# code (.csx) inside the sandbox. No Python is installed here — the agent
# is expected to solve tasks in C#.
#
# Build context is the REPO ROOT, not this directory: AmlAgent.csproj has a
# ProjectReference to ../../src/AmlAgent.Evidence/AmlAgent.Evidence.csproj
# (the EGHR/evidence-traceability scoring library), which must be visible to
# the build. src/AmlAgent.Harness/Program.cs (ResolveAgentImage) builds this
# image with `docker build -f agents/csharp-sk/Dockerfile <repoRoot>`.

FROM mcr.microsoft.com/dotnet/sdk:8.0-bookworm-slim AS build
WORKDIR /src
COPY agents/csharp-sk/AmlAgent.csproj agents/csharp-sk/
COPY src/AmlAgent.Evidence/AmlAgent.Evidence.csproj src/AmlAgent.Evidence/
RUN dotnet restore agents/csharp-sk/AmlAgent.csproj
COPY agents/csharp-sk/ agents/csharp-sk/
COPY src/AmlAgent.Evidence/ src/AmlAgent.Evidence/
RUN dotnet publish agents/csharp-sk/AmlAgent.csproj -c Release -o /out --no-restore

FROM mcr.microsoft.com/dotnet/sdk:8.0-bookworm-slim
RUN dotnet tool install -g dotnet-script \
 && ln -s /root/.dotnet/tools/dotnet-script /usr/local/bin/dotnet-script
ENV PATH="${PATH}:/root/.dotnet/tools"
WORKDIR /app
COPY --from=build /out /opt/agent
ENV BENCH_TASK_DIR=/app
ENTRYPOINT ["dotnet", "/opt/agent/aml-agent.dll", "run"]
