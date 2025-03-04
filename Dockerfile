# use "dotnet/sdk" to get the compiler for extensions
FROM mcr.microsoft.com/dotnet/sdk:9.0

ENV NUGET_XMLDOC_MODE=none

WORKDIR /app
COPY app .

USER app

# Create ~/.local/share so that it is definitely owned by
# the app user and Nuget packages can be restore to the
# ~.local/share/Nuget folder. Also there might be the need
# for extensions to write to this folder.
RUN mkdir -p /home/app/.local/share

ENTRYPOINT ["./Nexus"]