# Polyglot benchmark demo — a Python agent for AML-Agent-Bench.
#
# Demonstrates that the harness can run a non-C# agent against the same tasks
# and the same evaluators (xUnit + SK judge), supporting the cross-language
# comparison story in the proposal.

FROM python:3.11-slim
RUN pip install --no-cache-dir openai==1.59.6
COPY agent.py /opt/agent.py
ENV BENCH_TASK_DIR=/app
WORKDIR /app
ENTRYPOINT ["python", "/opt/agent.py"]
