# Deploying PharmaOS

One Linux server runs everything with Docker Compose: **SQL Server** (database), **API** (.NET) and **web**
(nginx serving the React app and forwarding `/api` to the API). HTTPS is terminated in front of it.

```
 browser ──HTTPS──▶ TLS proxy (Caddy / cloud LB) ──HTTP──▶ web (nginx :80) ──/api──▶ api (:8080) ──▶ db (SQL Server)
                                                                                     └── /data volume: MFA key ring + uploads
```

## 1. Server
- Ubuntu 22.04/24.04, **4 GB RAM minimum** (SQL Server needs 2 GB), 2 vCPU, 40 GB+ disk. Start with one region in India.
- Install Docker Engine + Compose plugin (`docs.docker.com/engine/install/ubuntu`).
- Firewall: open only 22 (SSH, your IP), 80 and 443.

## 2. First deployment
```bash
git clone https://github.com/<you>/EXPERTPH.git /opt/pharmaos && cd /opt/pharmaos
cp .env.example .env
nano .env                         # fill EVERY value — see below
docker compose up -d --build      # first build takes a few minutes
docker compose ps                 # db healthy, api + web running
curl -s localhost:8080/healthz          # web answers "ok"
```

`.env` values:

| Variable | What to put |
|---|---|
| `SQL_SA_PASSWORD` | Strong password (12+ chars, upper/lower/digit/symbol). |
| `JWT_KEY` | `openssl rand -base64 48` — **48+ characters**, never the development key. |
| `PLATFORM_ADMIN_EMAIL` / `PLATFORM_ADMIN_PASSWORD` | Your first SaaS-provider login. After first sign-in: turn on two-step login, then delete both lines and `docker compose up -d`. |
| `MSSQL_PID` | `Express` (free, 10 GB per database). Check licensing before choosing another edition. |
| `MIGRATE_ON_STARTUP` | `true` — the API upgrades the database schema on start. Take a backup before every upgrade. |
| `WEB_PORT` | Port the TLS proxy forwards to (default 8080). |

The API **refuses to start** in production if the JWT key or admin password is a sample/placeholder value.

## 3. HTTPS
Simplest: [Caddy](https://caddyserver.com) on the host, which gets certificates automatically.
```
# /etc/caddy/Caddyfile
app.yourdomain.in {
    reverse_proxy localhost:8080
}
```
Point the domain's DNS A-record to the server first. The offline billing mode (service worker) only works over HTTPS.

## 4. Backups — set up on day one
```bash
chmod +x deploy/*.sh
./deploy/backup.sh                                  # run once by hand and check backups/
crontab -e   →   30 2 * * * cd /opt/pharmaos && ./deploy/backup.sh >> backups/backup.log 2>&1
```
Each run writes a **pair**: `PharmaOS_<time>.bak` (database) and `appdata_<time>.tgz` (MFA key ring + uploaded
prescriptions). They must be restored together. Keeps 14 days locally — **copy `backups/` off the server daily**
(object storage, another machine). Restore drill monthly:
```bash
./deploy/restore.sh 20261119_023000      # asks you to type RESTORE
```

## 5. Upgrades
```bash
./deploy/backup.sh
git pull
docker compose up -d --build             # API applies new migrations on start
docker compose logs -f api               # watch for "Now listening"
```
Roll back = check out the previous commit and restore the backup taken just before the upgrade.

## 6. Day-to-day
| Task | Command |
|---|---|
| Status | `docker compose ps` |
| Logs | `docker compose logs --tail 200 api` |
| Restart API | `docker compose restart api` |
| Stop everything | `docker compose down` (data stays in volumes) |
| Disk usage | `docker system df` |

**Never** run `docker compose down -v` on a live server — `-v` deletes the database and key-ring volumes.

## 7. CI
`.github/workflows/ci.yml` runs on every push to `main` and on pull requests: backend integration tests against a real
SQL Server, frontend audit + type check + build, and a container build of both images. Keep `main` green before deploying.

## 8. Before the first paying customer
- [ ] HTTPS working, HSTS header present
- [ ] Backups running and copied off-server; one restore drill done
- [ ] Provider admin has two-step login on; bootstrap lines removed from `.env`
- [ ] Uptime check on `https://app.yourdomain.in/healthz` (any free monitor)
- [ ] CA / pharmacist review items in README completed
- [ ] Online-orders module stays off unless the compliance review is done
