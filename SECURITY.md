# Security policy

OpenVP runs uploaded SIMPL# programs as .NET code inside its own process, so a security bug can mean code running on the host machine.

## Reporting a vulnerability

Please **don't open a public issue** for security problems. Instead use GitHub's private reporting: go to the repository's **Security** tab and click **Report a vulnerability**.

Include what you found, how to reproduce it, and the version (shown in the console, or `/api/info`). You should get a reply within a week.

## Supported versions

Only the latest release gets fixes. The desktop app updates itself from the Releases page.

## Running OpenVP safely

- Keep the access key on (it is by default) and don't share it.
- Don't expose port 8080 to the internet; use it on a trusted network or behind a VPN.
- Only load programs you trust.
