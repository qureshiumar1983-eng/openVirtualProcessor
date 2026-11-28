namespace OpenVP.Server;

/// <summary>The line-per-endpoint permission map (see <see cref="EndpointPolicy"/>). Add a line here for every endpoint you register.</summary>
public static class PolicyTable
{
    public const string Text = """
        # No sign-in: sign-in itself, health, shared panels and incoming webhooks (their own token is the credential), the tunnel-safe pages.
        public GET /api/info
        public POST /api/login
        public GET /api/login/oidc
        public GET /.well-known/acme-challenge/{token}
        public GET /api/login/oidc/done
        public POST /api/logout
        public GET /healthz
        public * /hooks/{token}
        public POST /hooks/{token}
        public GET /s/{token}
        public GET /s/{token}/app
        public GET /s/{token}/app/{**path}
        public GET /s/{token}/openvp-crcomlib.js
        public POST /s/{token}/pin
        public GET /s/{token}/status
        public * /s/{token}/ws

        # Browser panels: open unless OpenVP:PanelsRequireKey, then they need Signal.
        panel * /ws/panel

        # The other end of a remote EISC link: an API token of scope eisc-link (or an Admin), and only when Remote EISC is on in Labs.
        Eisc * /ws/eisc

        # A SIMPL# program's web-scripting (CWS) server, at /cws/{slot}/...: needs Signal on that slot, unless the slot's "CWS is public" setting is on. Never on the tunnel port.
        cws * /cws/{n:int}
        cws * /cws/{n:int}/{**path}

        # MCP for AI assistants (OpenVP:McpEnabled): sign-in as for the API; each tool then checks the permission, slot and lock its REST twin needs (see Mcp/OpenVpTools).
        View POST /mcp/

        # Prometheus: open with OpenVP:MetricsPublic, else View.
        metrics GET /metrics

        # Read-only: lists, status, logs, files, versions, traces, metrics, and the debugger and metrics streams.
        View GET /api/activity
        View GET /api/alerts
        View GET /api/alerts/config
        View GET /api/alerts/status
        View GET /api/alerts/stream
        View GET /api/alerts/thresholds
        View GET /api/consoles
        View GET /api/demo
        View GET /api/devices
        View GET /api/discovery
        View GET /api/https/certificate
        View GET /api/compat
        View GET /api/examples
        View GET /api/integrations
        View GET /api/integrations/artnet
        View GET /api/integrations/mqtt
        View GET /api/integrations/osc
        View GET /api/integrations/prometheus
        View GET /api/locks
        View GET /api/labs
        View GET /api/log
        Admin GET /api/cip/trace
        View POST /api/me/password
        View GET /api/metrics
        View GET /api/metrics/history
        View GET /api/network
        View GET /api/panels
        View GET /api/plugins
        View * /api/plugins/{name}/{**path}
        View GET /api/panels/sg
        View GET /api/rack
        View GET /emulate/{ps:int}/{pi}/as/{ts:int}/{ti}
        View GET /emulate/{ps:int}/{pi}/as/{ts:int}/{ti}/{**path}
        View GET /api/slots/{n:int}/autopanel
        View GET /api/slots/{n:int}/layouts
        View GET /api/slots/{n:int}/layouts/{name}
        View GET /api/slots/{n:int}/panels/{ipid}/sg
        View GET /api/slots/{n:int}/panels/{ipid}/sg/assets/{name}
        View GET /api/presence
        View GET /api/recent
        View POST /api/report/draft
        View POST /api/report/url
        View GET /api/reports/data
        View GET /api/reports/preview
        View GET /api/schedules
        View POST /api/schedules/preview
        View GET /api/search/signals
        View GET /api/slots
        View GET /api/slots/{n:int}/
        View GET /api/slots/{n:int}/bookmarks
        View GET /api/slots/{n:int}/compat
        View GET /api/slots/{n:int}/exceptions
        View GET /api/slots/{n:int}/files
        View GET /api/slots/{n:int}/files/download
        View GET /api/slots/{n:int}/files/missing
        View GET /api/slots/{n:int}/files/text
        View POST /api/slots/{n:int}/files/validate
        View GET /api/slots/{n:int}/graph
        View GET /api/slots/{n:int}/ipremap
        View GET /api/slots/{n:int}/iptable
        View GET /api/slots/{n:int}/monitor
        View GET /api/slots/{n:int}/never-changed
        View GET /api/slots/{n:int}/wiring
        View GET /api/slots/{n:int}/ports
        View GET /api/slots/{n:int}/ports/traffic
        View GET /api/slots/{n:int}/recording/status
        View GET /api/slots/{n:int}/recordings
        View GET /api/slots/{n:int}/recordings/{id}
        View GET /api/slots/{n:int}/recordings/{id}/download
        View GET /api/slots/{n:int}/recordings/{id}/report
        View GET /api/slots/{n:int}/recordings/{id}/reports
        View GET /api/slots/{n:int}/redirects
        View GET /api/slots/{n:int}/signals
        View GET /api/slots/{n:int}/signals/{name}
        View GET /api/slots/{n:int}/signals/{name}/stream
        View GET /api/slots/{n:int}/simulators
        View GET /api/slots/{n:int}/splus
        View GET /api/slots/{n:int}/splus/source
        View GET /api/slots/{n:int}/snapshots
        View GET /api/slots/{n:int}/sockets
        View GET /api/slots/{n:int}/sockets/{id:long}/traffic
        View GET /api/slots/{n:int}/step
        View GET /api/slots/{n:int}/summary
        View GET /api/slots/{n:int}/traces
        View GET /api/slots/{n:int}/traces/{id}
        View GET /api/slots/{n:int}/versions
        View POST /api/slots/{n:int}/versions/diff
        View GET /api/slots/{n:int}/versions/{id}/download
        View GET /api/slots/{n:int}/watches
        View GET /api/slots/{n:int}/why
        View GET /api/templates
        View GET /api/templates/{id}
        View GET /api/trash
        View GET /api/tunnel
        View GET /api/unsupported
        View GET /api/uptime
        View GET /api/uptime/heatmap
        View GET /api/versions/settings
        View GET /api/welcome
        View POST /api/welcome/dismiss
        View * /ws/metrics
        View * /ws/trace

        # Set or pulse signals (a panel, the debugger force, the REST API).
        Signal POST /api/slots/{n:int}/signals +lock
        Signal PUT /api/slots/{n:int}/signals/{name} +lock
        Signal POST /api/slots/{n:int}/signals/{name}/pulse +lock

        # Start, stop and restart a slot.
        Operate POST /api/slots/bulk
        Operate POST /api/slots/{n:int}/restart +lock
        Operate POST /api/slots/{n:int}/start
        Operate POST /api/slots/{n:int}/stop +lock

        # IP table identify and read-only logins; network discovery.
        IpTable DELETE /api/discovery
        IpTable POST /api/discovery
        IpTable POST /api/slots/{n:int}/iptable/{ipid}/clients/{cid}/login
        IpTable POST /api/slots/{n:int}/iptable/{ipid}/identify

        # Shared-panel links.
        Share GET /api/shares
        Share POST /api/shares
        Share DELETE /api/shares/{id}
        Share POST /api/shares/{id}/undo

        # Acknowledge and mute alerts.
        AlertAck POST /api/alerts/{id}/ack
        AlertAck POST /api/alerts/{id}/mute
        AlertAck POST /api/alerts/{id}/unmute

        # Lock and unlock a slot.
        LockSlot DELETE /api/slots/{n:int}/lock
        LockSlot POST /api/slots/{n:int}/lock

        # Load, unload, roll back and check programs; slot settings; CH5 projects.
        LoadProgram POST /api/check
        LoadProgram POST /api/check/path
        LoadProgram POST /api/demo +all
        LoadProgram POST /api/examples/{id}/load
        LoadProgram POST /api/inspect
        LoadProgram DELETE /api/recent/{id}
        LoadProgram DELETE /api/slots/{n:int}/panels/{ipid}/ch5
        LoadProgram POST /api/slots/{n:int}/panels/{ipid}/ch5
        LoadProgram POST /api/slots/{n:int}/panels/{ipid}/sg
        LoadProgram DELETE /api/slots/{n:int}/panels/{ipid}/sg
        LoadProgram PUT /api/slots/{n:int}/layouts/{name}
        LoadProgram DELETE /api/slots/{n:int}/layouts/{name}
        LoadProgram POST /api/slots/{n:int}/program +lock
        LoadProgram POST /api/slots/{n:int}/program/path +lock
        LoadProgram POST /api/slots/{n:int}/program/recent/{id} +lock
        LoadProgram PUT /api/slots/{n:int}/room
        LoadProgram PUT /api/slots/{n:int}/settings
        LoadProgram POST /api/slots/{n:int}/snapshots/{id}/restore +lock
        LoadProgram POST /api/slots/{n:int}/unload +lock
        LoadProgram DELETE /api/slots/{n:int}/versions/{id}
        LoadProgram PUT /api/slots/{n:int}/versions/{id}
        LoadProgram POST /api/slots/{n:int}/versions/{id}/load +lock
        LoadProgram DELETE /api/slots/{n:int}/watch

        # Stepping, watches, monitors, traces, bookmarks, exceptions, support bundle.
        Debug PUT /api/slots/{n:int}/bookmarks/note
        Debug PUT /api/slots/{n:int}/bookmarks/star
        Debug DELETE /api/slots/{n:int}/exceptions
        Debug PUT /api/slots/{n:int}/monitor
        Debug DELETE /api/slots/{n:int}/monitor/alerts
        Debug POST /api/slots/{n:int}/never-changed/reset
        Debug POST /api/slots/{n:int}/step
        Debug POST /api/slots/{n:int}/step/next
        Debug POST /api/slots/{n:int}/step/run
        Debug GET /api/slots/{n:int}/support-bundle
        Debug POST /api/slots/{n:int}/trace/export
        Debug POST /api/slots/{n:int}/traces
        Debug POST /api/slots/{n:int}/traces/compare
        Debug DELETE /api/slots/{n:int}/traces/{id}
        Debug PUT /api/slots/{n:int}/traces/{id}
        Debug PUT /api/slots/{n:int}/watches
        Debug POST /api/slots/{n:int}/watches/check
        Debug DELETE /api/slots/{n:int}/watches/hits

        # NVRAM and User files; restoring from the trash.
        Files DELETE /api/slots/{n:int}/files +lock
        Files POST /api/slots/{n:int}/files +lock
        Files POST /api/slots/{n:int}/files/folder +lock
        Files POST /api/slots/{n:int}/files/rename +lock
        Files PUT /api/slots/{n:int}/files/text +lock
        Files POST /api/trash/{id}/restore

        # Record and replay.
        Recording POST /api/slots/{n:int}/recording/start
        Recording POST /api/slots/{n:int}/recording/stop
        Recording POST /api/slots/{n:int}/recordings
        Recording DELETE /api/slots/{n:int}/recordings/{id}
        Recording PUT /api/slots/{n:int}/recordings/{id}
        Recording POST /api/slots/{n:int}/recordings/{id}/replay +lock
        Recording POST /api/slots/{n:int}/replay/cancel

        # Take and delete snapshots.
        Snapshot POST /api/slots/{n:int}/snapshots
        Snapshot DELETE /api/slots/{n:int}/snapshots/{id}

        # Device simulators, COM ports, redirects, IP ID remap.
        Simulator PUT /api/slots/{n:int}/ipremap
        Simulator DELETE /api/slots/{n:int}/ipremap/{ipid}
        Simulator POST /api/slots/{n:int}/ports/clear
        Simulator POST /api/slots/{n:int}/ports/send
        Simulator POST /api/slots/{n:int}/redirects
        Simulator DELETE /api/slots/{n:int}/redirects/{id}
        Simulator PUT /api/slots/{n:int}/redirects/{id}
        Simulator POST /api/slots/{n:int}/simulators
        Simulator DELETE /api/slots/{n:int}/simulators/{id}
        Simulator PUT /api/slots/{n:int}/simulators/{id}
        Simulator POST /api/slots/{n:int}/simulators/{id}/state
        Simulator POST /api/slots/{n:int}/simulators/{id}/try

        # A slot's integrations and incoming webhooks.
        Integration GET /api/hooks +all
        Integration POST /api/hooks +all
        Integration DELETE /api/hooks/{id} +all
        Integration PUT /api/hooks/{id} +all
        Integration GET /api/slots/{n:int}/integrations
        Integration PUT /api/slots/{n:int}/integrations
        Integration POST /api/slots/{n:int}/webhooks/{id}/test
        Integration GET /api/webhooks/deliveries +all

        # Run a console command in a slot.
        Console POST /api/slots/{n:int}/console

        # Create, change and run schedules.
        Schedule POST /api/schedules +all
        Schedule DELETE /api/schedules/{id} +all
        Schedule PUT /api/schedules/{id} +all
        Schedule POST /api/schedules/{id}/run +all

        # Users, tokens, security, HTTPS, single sign-on, audit log, export and import, backups, push, git, alerts setup, tunnel, global integrations.
        Admin POST /api/alerts/channels
        Admin DELETE /api/alerts/channels/{id}
        Admin PUT /api/alerts/channels/{id}
        Admin POST /api/alerts/channels/{id}/test
        Admin PUT /api/alerts/config
        Admin GET /api/audit
        Admin GET /api/audit/download
        Admin POST /api/audit/verify
        Admin GET /api/backups
        Admin POST /api/backups/run
        Admin PUT /api/backups/settings
        Admin DELETE /api/backups/{id}
        Admin GET /api/backups/{id}/download
        Admin POST /api/backups/{id}/preview
        Admin POST /api/export
        Admin GET /api/eisc-links
        Admin POST /api/eisc-links
        Admin PUT /api/eisc-links/{id}
        Admin DELETE /api/eisc-links/{id}
        Admin PUT /api/labs/{id}
        Admin POST /api/plugin-admin/{name}/enable
        Admin POST /api/plugin-admin/{name}/disable
        Admin GET /api/git
        Admin PUT /api/git
        Admin POST /api/git/commit
        Admin POST /api/git/init
        Admin POST /api/git/push
        Admin GET /api/https
        Admin PUT /api/https
        Admin POST /api/https/acme
        Admin POST /api/https/self-signed
        Admin POST /api/https/upload
        Admin POST /api/import
        Admin POST /api/import/preview
        Admin DELETE /api/import/{id}
        Admin PUT /api/integrations/artnet
        Admin PUT /api/integrations/mqtt
        Admin PUT /api/integrations/osc
        Admin GET /api/oidc
        Admin PUT /api/oidc
        Admin GET /api/push
        Admin POST /api/push
        Admin GET /api/push/{id}
        Admin POST /api/reports/send
        Admin GET /api/security
        Admin PUT /api/security
        Admin GET /api/targets
        Admin POST /api/targets
        Admin POST /api/targets/certificate
        Admin DELETE /api/targets/{id}
        Admin PUT /api/targets/{id}
        Admin POST /api/targets/{id}/test
        Admin GET /api/tokens
        Admin POST /api/tokens
        Admin DELETE /api/tokens/{id}
        Admin POST /api/tunnel/start
        Admin POST /api/tunnel/stop
        Admin GET /api/users
        Admin POST /api/users
        Admin DELETE /api/users/{id}
        Admin PUT /api/users/{id}
        Admin POST /api/users/{id}/password
        Admin POST /api/users/{id}/signout
        Admin POST /api/users/{id}/unlock
        Admin PUT /api/versions/settings

        """;
}
