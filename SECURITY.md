# Security policy

## Reporting a vulnerability

**Please do not report security vulnerabilities through public GitHub issues, discussions or pull requests.**

Use GitHub's **Private vulnerability reporting** instead:

1. go to the [Security tab](https://github.com/dummy1969/vedettavip/security) of the repository;
2. click **Report a vulnerability**;
3. describe the issue: affected component and version (or commit), steps to reproduce, impact, and a proof of concept
   if you have one.

The report is visible only to the maintainers. You will receive an acknowledgement as soon as possible; we will work
with you on a fix and agree on the disclosure date. Credit is given in the advisory unless you prefer to remain
anonymous.

## Supported versions

VedettaVip is developed on a single branch: security fixes are made on the latest version of `main`. Please verify
that the issue is still present there before reporting it.

## Scope

Examples of relevant issues:

- authentication or authorization bypass (roles Admin / Operator / Viewer, agent key `X-Agent-Key`);
- disclosure of stored secrets (SNMP communities, RouterOS passwords, SMTP password, Telegram token) or of session
  cookies;
- CSRF, XSS or injection in the API or in the web interface;
- remote code execution or privilege escalation in the containers (the Worker runs with `CAP_NET_RAW`).

Not in scope: vulnerabilities of third-party components that are already fixed upstream (please just tell us to
upgrade), and installations exposed to the internet without the HTTPS reverse proxy described in
[deploy/production/README.md](deploy/production/README.md).
