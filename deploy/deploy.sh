#!/usr/bin/env bash
#
# The only thing the deployment key is allowed to do.
#
# Install it on the server as ~/deploy.sh, owned by the login user and not writable by
# anything else, then restrict the key in ~/.ssh/authorized_keys to it:
#
#   restrict,command="/home/xeon/deploy.sh" ssh-ed25519 AAAA...  github-actions
#
# "restrict" refuses port forwarding, agent forwarding, X11 and a terminal. "command"
# replaces whatever the client asked for, so the key cannot open a shell, copy a file or
# run anything else. What the client sent is readable in SSH_ORIGINAL_COMMAND and is
# treated here as untrusted input.
#
# This file must not be writable through that key, or the restriction means nothing: a
# deployment that could rewrite the script could put anything in it. Update it by hand.
#
#   sudo install -o xeon -g xeon -m 0755 deploy.sh /home/xeon/deploy.sh
#
set -euo pipefail

readonly PROJECT=xeon-cms
readonly DIRECTORY="$HOME/xeon-cms"
readonly BACKUPS="$HOME/backups"
readonly REPOSITORY=xeons/website
readonly REGISTRY="ghcr.io/${REPOSITORY}"
readonly KEEP_BACKUPS=14
readonly HEALTH_ATTEMPTS=30

log() {
    printf '%s  %s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$*"
}

fail() {
    log "FAILED: $*"
    exit 1
}

# The commit is the whole input. Anything else the client sends is refused, so a key that
# has been taken can still only deploy a commit of this repository, never an image someone
# else built.
commit="${SSH_ORIGINAL_COMMAND:-}"

if [[ ! "$commit" =~ ^[0-9a-f]{40}$ ]]; then
    fail "expected a commit, got: ${commit:-(nothing)}"
fi

readonly commit
readonly image="${REGISTRY}:sha-${commit}"

# One deployment at a time, however many arrive at once.
exec 9> "$HOME/.deploy.lock"
flock -n 9 || fail "another deployment is already running"

cd "$DIRECTORY"

log "deploying $commit"

# The compose file belongs to the commit being deployed rather than to whatever the server
# happens to be holding. The repository is public, so this needs no credentials. Written
# beside the real one and moved into place only once it has arrived whole.
log "fetching the compose file"
curl -fsS --max-time 30 \
    "https://raw.githubusercontent.com/${REPOSITORY}/${commit}/docker-compose.yml" \
    -o docker-compose.yml.incoming \
    || fail "could not fetch the compose file for $commit"

mv docker-compose.yml.incoming docker-compose.yml

# Before anything changes. Migrations run as the application starts, so a bad one is felt
# during the rollout below and this is what makes it recoverable.
log "backing up the database"
mkdir -p "$BACKUPS"
backup="${BACKUPS}/xeon-$(date -u +%Y%m%d-%H%M%S).sql.gz"

docker compose -p "$PROJECT" exec -T db pg_dump -U xeon -d xeon | gzip > "$backup" \
    || fail "the database backup failed"

log "backed up to $backup ($(du -h "$backup" | cut -f1))"
ls -t "${BACKUPS}"/*.sql.gz | tail -n "+$((KEEP_BACKUPS + 1))" | xargs -r rm --

# Pulled before .env is pointed at it. Naming an image the server does not have and only
# then finding out it cannot be fetched would leave the file describing something absent,
# and the next restart would fail to start the site.
log "pulling $image"
docker pull "$image" || fail "could not pull $image"

previous="$(grep '^APP_IMAGE=' .env | cut -d= -f2- || true)"

sed -i '/^APP_IMAGE=/d' .env
printf 'APP_IMAGE=%s\n' "$image" >> .env

log "starting"
docker compose -p "$PROJECT" up -d || fail "the stack did not come up"

log "waiting for the application to answer"
for attempt in $(seq 1 "$HEALTH_ATTEMPTS"); do
    if curl -fsS -o /dev/null --max-time 5 http://127.0.0.1:8080/health; then
        log "healthy after ${attempt} attempt(s)"
        docker image prune -f > /dev/null

        # Left in the log rather than acted on: rolling back an image after its migrations
        # have run against the database is not safe to do unattended.
        log "deployed $commit, previously ${previous:-none}"
        exit 0
    fi
    sleep 2
done

fail "the application did not answer after $((HEALTH_ATTEMPTS * 2)) seconds, still on $image"
