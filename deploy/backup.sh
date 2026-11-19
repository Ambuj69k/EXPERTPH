#!/usr/bin/env bash
# Nightly backup: SQL database + the app data volume (MFA key ring, uploaded prescriptions).
# They must be restored TOGETHER — the key ring decrypts MFA secrets stored in the database.
# Cron example (02:30 daily):  30 2 * * * cd /opt/pharmaos && ./deploy/backup.sh >> backups/backup.log 2>&1
set -euo pipefail
cd "$(dirname "$0")/.."
set -a; . ./.env; set +a
TS=$(date +%Y%m%d_%H%M%S)
DB=${SQL_DATABASE:-PharmaOS}
KEEP_DAYS=${KEEP_DAYS:-14}
mkdir -p backups

echo "[$TS] backing up database $DB"
docker compose exec -T db /opt/mssql-tools18/bin/sqlcmd -C -b -S localhost -U sa -P "$SQL_SA_PASSWORD" \
  -Q "BACKUP DATABASE [$DB] TO DISK = N'/backups/${DB}_${TS}.bak' WITH CHECKSUM, INIT; RESTORE VERIFYONLY FROM DISK = N'/backups/${DB}_${TS}.bak' WITH CHECKSUM;"

echo "[$TS] backing up app data volume"
docker run --rm -v pharmaos_appdata:/data:ro -v "$PWD/backups":/out alpine tar czf "/out/appdata_${TS}.tgz" -C /data .

echo "[$TS] removing backups older than $KEEP_DAYS days"
find backups -maxdepth 1 \( -name '*.bak' -o -name 'appdata_*.tgz' \) -mtime +"$KEEP_DAYS" -delete

echo "[$TS] done. Copy backups/ OFF this server (object storage / another machine) — a backup on the same disk is not a backup."
