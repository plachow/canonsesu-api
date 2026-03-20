# Server Setup

Soubory pro nasazení na produkční server.

## Postup

### 1. Zkopírovat soubory na server

```bash
scp docker-compose.yml user@server:/opt/docker/apps/sesu-api/
```

### 2. Vytvořit .env ze šablony

```bash
cp .env.template /opt/docker/apps/sesu-api/.env
# Vyplnit hodnoty
nano /opt/docker/apps/sesu-api/.env
```

### 3. GHCR login na serveru (jednorázově)

```bash
echo "GITHUB_PAT" | docker login ghcr.io -u plachow --password-stdin
```

GitHub PAT: GitHub → Settings → Developer settings → Personal access tokens → scope `read:packages`

### 4. Spustit kontejner

```bash
cd /opt/docker/apps/sesu-api
docker compose up -d
```

### 5. Ověřit

```bash
docker ps
curl http://localhost:5080/health
```

## Watchtower

Watchtower automaticky stahuje nový `:latest` image po každém CI/CD buildu.
Kontejner musí mít label `com.centurylinklabs.watchtower.enable=true` (již nastaveno v docker-compose.yml).
