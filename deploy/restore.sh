#!/usr/bin/env bash
# Restore a backup pair taken by backup.sh. Usage: ./deploy/restore.sh 20261119_023000
# Stops the API, restores the database and the app data volume, then starts the API again.
set -euo pipefail
cd "$(dirname "$0")/.."
TS=${1:?usage: restore.sh <timestamp from the backup file names>}
set -a; . ./.env; set +a
DB=${SQL_DATABASE:-PharmaOS}
test -f "backups/${DB}_${TS}.bak" && test -f "backups/appdata_${TS}.tgz" || { echo "backup pair for $TS not found in backups/"; exit 1; }
read -r -p "This REPLACES the live database $DB and app data with the $TS backup. Type RESTORE to continue: " ok
[ "$ok" = "RESTORE" ] || { echo "cancelled"; exit 1; }

docker compose stop api
docker compose exec -T db /opt/mssql-tools18/bin/sqlcmd -C -b -S localhost -U sa -P "$SQL_SA_PASSWORD" \
  -Q "ALTER DATABASE [$DB] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; RESTORE DATABASE [$DB] FROM DISK = N'/backups/${DB}_${TS}.bak' WITH REPLACE, CHECKSUM; ALTER DATABASE [$DB] SET MULTI_USER;"
docker run --rm -v pharmaos_appdata:/data -v "$PWD/backups":/in alpine sh -c "rm -rf /data/* && tar xzf /in/appdata_${TS}.tgz -C /data && chown -R 1654:1654 /data"
docker compose start api
echo "restored $TS — sign in and check today's bills, stock and two-step login."
