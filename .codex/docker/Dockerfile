# .codex/docker/Dockerfile
FROM mcr.microsoft.com/dotnet/sdk:8.0

# Node (si vous exécutez aussi des tests front)
# RUN apt-get update && apt-get install -y curl ca-certificates gnupg && \
#     curl -fsSL https://deb.nodesource.com/setup_20.x | bash - && \
#     apt-get install -y nodejs && npm -v && node -v

WORKDIR /workspace
CMD ["bash", "-lc", "bash ./run_tests.sh"]
