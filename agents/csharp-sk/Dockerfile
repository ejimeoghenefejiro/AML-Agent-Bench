# Sandbox image: C# agent + Python data stack so the agent can author and run analysis code.
FROM mcr.microsoft.com/dotnet/sdk:8.0-bookworm-slim AS build
WORKDIR /src
COPY AmlAgent.csproj ./
RUN dotnet restore
COPY . .
RUN dotnet publish AmlAgent.csproj -c Release -o /out --no-restore

FROM mcr.microsoft.com/dotnet/runtime:8.0-bookworm-slim
RUN apt-get update \
 && apt-get install -y --no-install-recommends python3 python3-pip bash ca-certificates \
 && rm -rf /var/lib/apt/lists/* \
 && ln -sf /usr/bin/python3 /usr/bin/python \
 && pip3 install --no-cache-dir --break-system-packages pandas numpy networkx pytest

WORKDIR /app
COPY --from=build /out /opt/agent
ENV BENCH_TASK_DIR=/app
ENTRYPOINT ["dotnet", "/opt/agent/aml-agent.dll"]
