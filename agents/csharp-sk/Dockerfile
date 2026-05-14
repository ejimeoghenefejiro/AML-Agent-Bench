# Sandbox image for the C# / Semantic Kernel agent.
# Includes the .NET 8 SDK + dotnet-script so the agent can author and execute
# C# code (.csx) inside the sandbox. No Python is installed here — the agent
# is expected to solve tasks in C#.

FROM mcr.microsoft.com/dotnet/sdk:8.0-bookworm-slim AS build
WORKDIR /src
COPY AmlAgent.csproj ./
RUN dotnet restore
COPY . .
RUN dotnet publish AmlAgent.csproj -c Release -o /out --no-restore

FROM mcr.microsoft.com/dotnet/sdk:8.0-bookworm-slim
RUN dotnet tool install -g dotnet-script \
 && ln -s /root/.dotnet/tools/dotnet-script /usr/local/bin/dotnet-script
ENV PATH="${PATH}:/root/.dotnet/tools"
WORKDIR /app
COPY --from=build /out /opt/agent
ENV BENCH_TASK_DIR=/app
ENTRYPOINT ["dotnet", "/opt/agent/aml-agent.dll", "run"]
