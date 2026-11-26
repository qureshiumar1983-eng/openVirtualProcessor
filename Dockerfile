FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
# The publish output includes the sample programs and the starter templates (see src/OpenVP.Server/OpenVP.Server.csproj), so Try the demo and Templates work in the container.
RUN dotnet publish src/OpenVP.Server -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:8.0
# git is for Git-backed config (Backups > Git-backed config); leave it out with --build-arg WITH_GIT=false and the feature shows as unavailable.
ARG WITH_GIT=true
RUN if [ "$WITH_GIT" = "true" ]; then \
      apt-get update && apt-get install -y --no-install-recommends git openssh-client ca-certificates && rm -rf /var/lib/apt/lists/*; \
    fi
WORKDIR /app
COPY --from=build /out .
# 8080 web console and REST API, 41794 CIP (touch panels), 49200 CIP over a secure WebSocket (Crestron Virtual Panel, HTML5 Web XPanel), 41795 telnet console and 22 SSH console (both off unless enabled with OpenVP__TelnetEnabled / OpenVP__SshEnabled).
EXPOSE 8080 41794 49200 41795 22
VOLUME /app/data
# "openvp --healthcheck" asks the server on its own console port for /healthz and exits 0 or 1 (there is no curl in this image).
HEALTHCHECK --interval=30s --timeout=6s --start-period=30s --retries=3 CMD ["dotnet", "openvp.dll", "--healthcheck"]
ENTRYPOINT ["dotnet", "openvp.dll"]
