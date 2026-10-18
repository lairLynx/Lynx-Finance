# Hosting Lynx Finance (Windows + Tailscale)

Lynx runs as a Windows service on one machine, reachable only from your tailnet. Pushing to the `prod`
branch builds and tests a release on GitHub; the host notices it within five minutes, swaps it in, checks
that it is healthy and rolls back by itself if it is not.

```
push to prod ──► GitHub Actions (tests + build) ──► GitHub Release (zip + sha256)
                                                          │  polled every 5 min
                                                          ▼
 phone/laptop ──► Tailscale ──► tailscale serve :8443 ──► Lynx service 127.0.0.1:5180
   (tailnet)      (TLS)                                   C:\Apps\Lynx\current\Lynx.Api.exe
                                                          C:\Apps\Lynx\data\lynx.db   ← never touched by deploys
```

Why this shape: the repo is public, so a self-hosted Actions runner on your machine would be a risk (anyone
could try to run code on it through a pull request). Instead GitHub builds in its own sandbox and your
machine only *downloads* the result over HTTPS and verifies its checksum. Nothing on the host is reachable
from the internet: Kestrel listens on loopback only, and Tailscale is the only way in.

## Ports (so several projects can share this machine)

| Project | Kestrel (loopback only) | Tailnet port |
|---|---|---|
| Lynx Finance | 5180 | 8443 |
| next project | 5181 | 8444 |
| … | 518x | 844x |

Pick a unique pair per project: `Install-Lynx.ps1 -LocalPort 5181 -TailnetPort 8444 -ServiceName OtherApp`.
Each project then lives at `https://<machine>.<tailnet>.ts.net:<tailnet port>`.
If Tailscale refuses a port in `Https` mode, use `-ServeMode Tcp` (plain HTTP over the already
encrypted tailnet; accepts any port).

## One-time setup

### 1. GitHub
1. Commit your work and push it (`master`).
2. Create the `prod` branch: `git switch -c prod && git push -u origin prod`.
3. **Protect `prod`** (Settings → Branches → Add rule for `prod`: require a pull request, no force pushes).
   Whatever lands on `prod` is deployed and runs on your machine, so only you should be able to push there.
4. Settings → Actions → General: "Workflow permissions" can stay read-only (the workflow requests the
   `contents: write` it needs); under "Fork pull request workflows" keep "Require approval".
5. The first push to `prod` produces the first release (Actions tab → "Release (prod)").

### 2. Tailscale (on the host)
Install Tailscale and sign in. In the admin console (DNS page) enable **MagicDNS** and **HTTPS Certificates**.
These are needed for `Https` mode.

### 3. Install (on the host, elevated PowerShell)
Get this `deploy` folder onto the host (clone the repo, or copy the folder), then:

```powershell
# On your dev machine: stop the dev API, then copy src\Lynx.Api\App_Data\lynx.db (and lynx.db-wal / -shm if present)
# to the host, e.g. D:\transfer\lynx.db

# On the host:
.\Install-Lynx.ps1 -DatabasePath D:\transfer\lynx.db -TailnetPort 8443
```

It creates `C:\Apps\Lynx`, imports the database, installs the `LynxFinance` service (virtual account, loopback
only, restarts on failure), deploys the latest release, publishes it with `tailscale serve`, and registers
the auto-update task. It prints the URL when done. You will have to sign in again on the new host (session
cookies are tied to the host); your password is unchanged.

No release yet? Build the package yourself and pass it in: `.\Build-Release.ps1`, then
`.\Install-Lynx.ps1 -ZipPath ..\artifacts\lynx-xxxxxxx.zip`.

## Day to day

- **Deploy:** merge into `prod`. Within ~5 minutes the host updates. Watch `C:\Apps\Lynx\logs\deploy.log`.
- **Deploy right now:** `C:\Apps\Lynx\current\deploy\Update-Lynx.ps1`
- **Roll back by hand:** `Update-Lynx.ps1 -Tag prod-12-abc1234` (the host keeps the last 3 releases; older
  ones are still on GitHub). This **pins** that release so the scheduler leaves it alone; `Update-Lynx.ps1 -Unpin` resumes updates.
- **Automatic rollback:** if a new release does not report healthy on `/healthz` within 60 s, the previous
  release *and the database as it was before the deploy* are restored, and the failure is logged.
- **Logs:** `deploy.log` for deploys; Event Viewer → Windows Logs → Application → source `LynxFinance` for the app.
- **Health:** `curl http://127.0.0.1:5180/healthz` on the host returns `{"status":"ok","version":"<commit>"}`.
- **Remove:** `Uninstall-Lynx.ps1` (keeps `data\` and `backups\` unless `-DeleteData`).

## Data and backups

Everything that matters is in `C:\Apps\Lynx\data` (`lynx.db`, `keys\`). Deploys never touch it, and a copy of
the database is taken before every deploy into `backups\` (last 14 kept). Those backups live on the same disk,
so **also copy `data\` somewhere else regularly** (another disk, or your usual backup tool). Stop the service
first if you want a perfectly consistent copy of a SQLite file: `Stop-Service LynxFinance`.

## Things to know

- Database migrations run automatically when the new version starts. They only move forward, which is why a
  rollback restores the pre-deploy database copy: anything entered between the deploy and the rollback is lost.
- The release is self-contained (the .NET runtime is bundled), so the host needs nothing installed besides
  Tailscale. Runtime security patches arrive with the next deploy, so redeploy now and then.
- Logging in over `Https` mode marks the cookie `Secure`. Opening `http://127.0.0.1:5180` directly on the host
  still works in modern browsers (they treat localhost as secure).
- The update task runs as SYSTEM and downloads from `github.com/<Repo>/releases`; change `Repo` in
  `C:\Apps\Lynx\config.json` if you move the repository.
- Two-factor login and the bank connector are not part of this deployment yet.
