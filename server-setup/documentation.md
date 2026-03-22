# Technická dokumentace — CanonSeSu.Api

Kompletní přehled architektury, implementace a provozu systému pro hlášení stavu počítadel tiskáren Canon CZ s.r.o.

---

## Obsah

1. [Přehled systému](#1-přehled-systému)
2. [Technologický stack](#2-technologický-stack)
3. [Architektura](#3-architektura)
4. [Databáze](#4-databáze)
5. [API endpointy](#5-api-endpointy)
6. [Emailová služba](#6-emailová-služba)
7. [Plánovač úloh](#7-plánovač-úloh)
8. [Frontend](#8-frontend)
9. [Bezpečnost](#9-bezpečnost)
10. [CI/CD a verzování](#10-cicd-a-verzování)
11. [Infrastruktura a nasazení](#11-infrastruktura-a-nasazení)
12. [Konfigurace](#12-konfigurace)
13. [Testování emailů](#13-testování-emailů)
14. [Provozní postupy](#14-provozní-postupy)

---

## 1. Přehled systému

**CanonSeSu.Api** je webová služba pro automatizované měsíční hlášení stavu počítadel tiskáren zákazníky Canon CZ s.r.o.

### Procesní tok

```
1. Zákazník (MSSQL)  →  POST /api/devices/bulk  →  Databáze (PostgreSQL)
2. 28. v měsíci 02:00 CET  →  Quartz job  →  Email uživatelům (SES)
3. Uživatel klikne odkaz  →  Vyplní formulář  →  POST /api/user/{idcode}
```

### Klíčové vlastnosti

- Jeden přístupový odkaz (idcode) pokrývá všechna zařízení daného příjemce
- Přeposílání hlášení je povoleno (uživatel může opravit chybně zadané hodnoty)
- Hlášení po termínu je blokováno (HTTP 422)
- Automatické emaily každý měsíc přes Amazon SES
- Watchtower automaticky nasazuje nové verze po každém CI/CD buildu

---

## 2. Technologický stack

| Vrstva | Technologie |
|---|---|
| Framework | .NET 10 Minimal API |
| Databáze | PostgreSQL 18 (Docker, Linux) |
| ORM | LINQ2DB |
| Email | Amazon SES v2 (HTTPS/443) |
| Scheduler | Quartz.NET |
| Frontend | Vanilla HTML/CSS/JS (statické soubory) |
| Kontejnerizace | Docker + GitHub Container Registry (GHCR) |
| Reverse proxy | Traefik v3 |
| Tunnel | Cloudflare Tunnel |
| CI/CD | GitHub Actions |
| Auto-deploy | Watchtower |

---

## 3. Architektura

### Adresářová struktura

```
CanonSeSu.Api/
├── Data/
│   ├── AppDb.cs                     # LINQ2DB kontext
│   └── Models/
│       └── ServiceDeviceCounter.cs  # Model tabulky
├── Endpoints/
│   ├── UserEndpoints.cs             # Veřejné endpointy (/api/user/*, /api/info)
│   ├── AdminEndpoints.cs            # Admin endpointy (vyžadují API klíč)
│   └── DevicesEndpoints.cs          # Přehled zařízení
├── Jobs/
│   └── CounterRequestEmailJob.cs    # Quartz job — odesílání emailů
├── Middleware/
│   └── ApiKeyMiddleware.cs          # Ověření API klíče
├── Services/
│   └── EmailService.cs              # Odesílání přes Amazon SES v2
├── wwwroot/
│   ├── index.html                   # Úvodní stránka portálu
│   ├── pocitadla.html               # Formulář pro hlášení počítadel
│   ├── css/style.css
│   ├── js/app.js
│   ├── favicon.svg
│   └── canon-logo.png
├── Program.cs
├── appsettings.json
├── appsettings.Development.json
├── Dockerfile
└── .github/workflows/release.yml
```

### Request pipeline

```
Request
  → Traefik (reverse proxy)
  → UseDefaultFiles / UseStaticFiles  (statické soubory z wwwroot/)
  → UseRateLimiter                    (60 req/min, fixed window)
  → ApiKeyMiddleware                  (přeskočeno pro /api/user/*, /api/info, /health)
  → Endpoint handlers
```

---

## 4. Databáze

**Database:** `canon_services_support`
**Table:** `service_device_counters`

### Schéma tabulky

| Sloupec | Typ | Popis |
|---|---|---|
| `recordid` | int (PK) | Primární klíč, automaticky generovaný |
| `email` | text | Email příjemce hlášení |
| `idcode` | text | Přístupový kód — jeden kód pro všechna zařízení jednoho emailu v daném období |
| `typkonfigurace` | text | Typ konfigurace stroje |
| `typstroje` | text | Obchodní název stroje |
| `vyrobnicislo` | text | Výrobní (sériové) číslo |
| `typpocitadla` | text | Kód typu počítadla (např. 122) |
| `nazevpocitadla` | text | Čitelný název počítadla (např. Color/Large (E)) |
| `datumposlednihohlaseni` | timestamp | Datum předchozího hlášení |
| `poslednistavpocitadla` | int | Hodnota počítadla z předchozího hlášení |
| `datumaktualnihohlaseni` | timestamp | Datum aktuálního období (typicky 1. den měsíce) |
| `deadlinedate` | timestamp | Termín odevzdání hlášení |
| `aktualnistavpocitadla` | int | Hodnota zadaná uživatelem — null dokud není odesláno |
| `poznamka` | text | Volitelná poznámka uživatele |
| `datumcasnahlaseni` | timestamp | Čas odeslání hlášení uživatelem (UTC) |

### Důležité principy

- **Aktuální období** = záznamy s `MAX(datumaktualnihohlaseni)`
- **Jeden idcode** pokrývá všechna zařízení pro daný email v daném období
- Při opakovaném vkládání pro stejné období přes bulk insert vznikají nové záznamy — starší zůstávají v databázi
- Opakované odeslání hlášení uživatelem je povoleno (přepíše `aktualnistavpocitadla`, `poznamka`, `datumcasnahlaseni`)

---

## 5. API endpointy

### Veřejné (bez API klíče)

#### `GET /health`
Health check. Vrací `200 Healthy`. Používán Dockerem / monitoringem.

#### `GET /api/info`
Vrací informace o aktuálním období bez nutnosti idcode. Používán úvodní stránkou portálu.

```json
{
  "period": "2026-03-01",
  "deadline": "2026-03-28",
  "isPastDeadline": false
}
```

#### `GET /api/user/{idcode}`
Vrací kompletní kontext pro formulář — zařízení, stav hlášení, termín.

```json
{
  "period": "2026-03-01",
  "deadline": "2026-03-28",
  "isPastDeadline": false,
  "alreadySubmitted": false,
  "devices": [
    {
      "recordId": 1,
      "typStroje": "IMAGEPRESS V700 SERIES",
      "vyrobniCislo": "4VB05026",
      "typKonfigurace": "V700",
      "typPocitadla": "122",
      "nazevPocitadla": "Color/Large (E)",
      "posledniStavPocitadla": 12345,
      "aktualniStavPocitadla": null,
      "poznamka": null
    }
  ]
}
```

Vrací `404` pro neznámý nebo expirovaný idcode.

#### `POST /api/user/{idcode}`
Odeslání hlášení. Přeposílání povoleno. Vrací `422` po termínu.

```json
[
  { "recordId": 1, "aktualniStavPocitadla": 13200, "poznamka": null }
]
```

---

### Admin (vyžadují hlavičku `X-Api-Key`)

#### `POST /api/devices/bulk`
Hromadné vložení zařízení pro nové období. API samo vygeneruje idcode (jeden per email).

Tělo požadavku — JSON pole:
```json
[
  {
    "email": "zakaznik@firma.cz",
    "typKonfigurace": "V700",
    "typStroje": "IMAGEPRESS V700 SERIES",
    "vyrobniCislo": "4VB05026",
    "typPocitadla": "122",
    "nazevPocitadla": "Color/Large (E)",
    "datumPoslednihoHlaseni": "2026-02-01",
    "posledniStavPocitadla": 12345,
    "datumAktualnihoHlaseni": "2026-03-01",
    "deadlineDate": "2026-03-28"
  }
]
```

Odpověď — jeden záznam per unikátní email:
```json
[{ "email": "zakaznik@firma.cz", "idCode": "a3f9c2d1..." }]
```

#### `GET /api/devices/current`
Seznam všech zařízení aktuálního období. Volitelné filtry: `?startDate=&endDate=`.

#### `GET /api/admin/status`
Přehled odevzdání pro aktuální období.

```json
{
  "period": "2026-03-01",
  "deadline": "2026-03-28",
  "isPastDeadline": false,
  "totalDevices": 250,
  "submitted": 180,
  "pending": 70,
  "totalRecipients": 62,
  "submittedRecipients": 45,
  "pendingRecipients": 17,
  "submissionRate": 72.0
}
```

#### `POST /api/admin/emails/trigger`
Manuální spuštění emailového jobu. Vrací `202 Accepted`, job běží na pozadí.

#### `POST /api/admin/emails/resend/{email}`
Opětovné odeslání emailu konkrétnímu příjemci z aktuálního období.

---

## 6. Emailová služba

**Soubor:** `Services/EmailService.cs`

Odesílá emaily přes **Amazon SES v2** — komunikace probíhá výhradně přes HTTPS (port 443), není tedy blokována poskytovateli serverů blokujícími SMTP port 25/465.

### Obsah emailu

- Canon-branded HTML šablona (inline CSS pro kompatibilitu s poštovními klienty)
- Tabulka zařízení příjemce
- Tlačítko s odkazem na formulář (`UserPortalBaseUrl?idcode={idcode}`)
- Nápověda k výpočtu počítadel Total BW / Total Colour
- Plaintext fallback

### Konfigurace

| Nastavení | Popis |
|---|---|
| `Email:FromAddress` | Odesílací adresa (musí být ověřená v SES) |
| `Email:FromName` | Zobrazované jméno odesílatele |
| `Email:ReplyToAddress` | Adresa pro odpovědi |
| `Email:UserPortalBaseUrl` | Základ URL portálu (bez lomítka na konci) |
| `Email:DryRun` | `true` = loguje, ale neodesílá do SES |
| `Email:OverrideRecipient` | Přesměruje všechny emaily na tuto adresu |

### Předmět emailu

- Standardně: `Hlášení stavu počítadel – březen 2026`
- S OverrideRecipient: `[TEST: original@firma.cz] Hlášení stavu počítadel – březen 2026`

---

## 7. Plánovač úloh

**Soubor:** `Jobs/CounterRequestEmailJob.cs`

Quartz.NET job s anotací `[DisallowConcurrentExecution]`.

- **Automatické spuštění:** 28. každého měsíce ve 2:00 CET
- **Cron výraz:** `0 0 2 28 * ?`
- **Časová zóna:** `Central European Standard Time`

### Postup při spuštění

1. Načte záznamy aktuálního období (`MAX(datumaktualnihohlaseni)`)
2. Seskupí podle emailu
3. Předá `EmailService` k odeslání
4. Chyba u jednoho příjemce nezablokuje ostatní

---

## 8. Frontend

Statické soubory v `wwwroot/`, servírované přímo .NET aplikací.

### Stránky

| Soubor | URL | Popis |
|---|---|---|
| `index.html` | `/` | Úvodní stránka portálu — pravidla hlášení, aktuální období, servisní odkazy |
| `pocitadla.html` | `/pocitadla.html` | Formulář pro vyplnění počítadel |

### Přesměrování

Odkaz z emailu vede na `/?idcode=abc123`. `index.html` detekuje parametr `idcode` a automaticky přesměruje na `/pocitadla.html?idcode=abc123`.

### Stavy formuláře (`pocitadla.html`)

| Stav | Chování |
|---|---|
| Načítání | Skeleton loader |
| Neplatný/expirovaný kód | Chybová stránka (404) |
| Po termínu | Varovný banner, formulář je disabled |
| Již odesláno | Info banner, opětovné odeslání možné po potvrzení |
| Úspěch | Děkovací stránka se souhrnem odeslaných hodnot |

### Validace formuláře

- Pole je povinné
- Hodnota musí být celé číslo
- Aktuální stav nesmí být nižší než předchozí stav počítadla
- Zobrazení rozdílu (+N kopií od minulého hlášení)
- Tlačítko Odeslat aktivní až po vyplnění všech polí

### Seskupování zařízení

Zařízení jsou seskupena podle kombinace `vyrobniCislo + typKonfigurace` (= jeden fyzický stroj). Pod hlavičkou stroje jsou zobrazeny řádky jednotlivých počítadel.

---

## 9. Bezpečnost

### API klíč

Middleware `ApiKeyMiddleware` vyžaduje hlavičku `X-Api-Key` pro všechny admin endpointy.

Výjimky (nevyžadují klíč):
- `/api/user/*`
- `/api/info`
- `/health`
- Statické soubory (`wwwroot/`)

### Rate limiting

Fixed window limiter: **60 požadavků za minutu** na endpoint. Překročení vrací HTTP 429.

### Idcode

- Generován jako `Guid.NewGuid().ToString("N")` — 32 hexadecimálních znaků
- Jeden kód pokrývá všechna zařízení daného emailu v daném období
- Po uplynutí deadlinu přestává být funkční pro odeslání (ale stránka stále funguje pro zobrazení)

---

## 10. CI/CD a verzování

### GitHub Actions workflow (`.github/workflows/release.yml`)

Spouští se při každém push na větev `main`.

**Kroky:**
1. Výpočet CalVer verze: `YYYY.M.{run_number}` (např. `2026.3.15`)
2. `dotnet restore` + `dotnet publish -c Release`
3. Přihlášení do GitHub Container Registry (GHCR)
4. Build a push Docker image se tagy `:YYYY.M.N` a `:latest`
5. Vytvoření GitHub Release s automatickými release notes

### Verzování

- Schéma: **CalVer** — `YYYY.M.{číslo_buildu}`
- Verze je vložena do sestavení přes `/p:Version=$VERSION`
- Každý push na `main` = nový build = nový release

### Potřebná nastavení repozitáře

GitHub → Settings → Actions → General → Workflow permissions: **Read and write permissions**

---

## 11. Infrastruktura a nasazení

### Přehled

```
Internet
  → Cloudflare (DNS + CDN)
  → Cloudflare Tunnel (cloudflared)
  → Traefik v3 (reverse proxy, Docker network: proxy)
  → sesu-api kontejner (port 8080)
```

### Docker Compose (`server-setup/docker-compose.yml`)

```yaml
services:
  sesu-api:
    image: ghcr.io/plachow/canonsesu-api:latest
    pull_policy: always
    environment: ...   # všechny proměnné z .env
    labels:
      - traefik.enable=true
      - traefik.http.routers.sesu-api.rule=Host(`pocitadla.services-support.cz`)
      - com.centurylinklabs.watchtower.enable=true
    networks: [proxy]

  watchtower:
    image: containrrr/watchtower
    volumes:
      - /var/run/docker.sock:/var/run/docker.sock
      - /root/.docker/config.json:/config.json  # přihlášení do GHCR
    environment:
      - WATCHTOWER_LABEL_ENABLE=true
      - WATCHTOWER_POLL_INTERVAL=300  # kontrola každých 5 minut
```

### Auto-deploy (Watchtower)

Watchtower každých 5 minut kontroluje, zda je na GHCR k dispozici nový `:latest` image. Pokud ano, stáhne ho a restartuje kontejner. Vyžaduje `config.json` s přihlašovacími údaji do GHCR.

### Cloudflare Tunnel

V Cloudflare Zero Trust → Networks → Tunnels → Public Hostnames:
- **Subdomain:** `pocitadla` / **Domain:** `services-support.cz`
- **Service:** `http://traefik:80`

### Nasazení na server (postup)

```bash
# Na serveru v /opt/docker/apps/sesu-api/
# 1. Zkopírovat docker-compose.yml a .env (přes SFTP)

# 2. Přihlásit se do GHCR (jednorázově — uloží credentials do /root/.docker/config.json)
echo "GITHUB_PAT" | docker login ghcr.io -u plachow --password-stdin

# 3. Spustit
docker compose up -d

# 4. Ověřit
docker ps
curl http://localhost:8080/health
```

**GitHub PAT** pro GHCR: GitHub → Settings → Developer settings → Personal access tokens → scope `read:packages`

---

## 12. Konfigurace

### `appsettings.json` — struktura (hodnoty prázdné, doplňují se v `.env`)

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=...;Database=canon_services_support;Username=...;Password=..."
  },
  "ApiKey": "...",
  "Aws": {
    "Region": "eu-central-1",
    "AccessKeyId": "",
    "SecretAccessKey": ""
  },
  "Email": {
    "FromAddress": "noreply@services-support.cz",
    "FromName": "Canon CZ s.r.o.",
    "ReplyToAddress": "servis@services-support.cz",
    "UserPortalBaseUrl": "https://pocitadla.services-support.cz",
    "DryRun": false,
    "OverrideRecipient": ""
  }
}
```

### `.env` na serveru (`server-setup/.env.template`)

```env
ConnectionStrings__DefaultConnection=Host=postgres;Database=canon_services_support;Username=...;Password=...
ApiKey=...
Aws__Region=eu-central-1
Aws__AccessKeyId=...
Aws__SecretAccessKey=...
Email__FromAddress=noreply@services-support.cz
Email__FromName=Canon CZ s.r.o.
Email__ReplyToAddress=servis@services-support.cz
Email__UserPortalBaseUrl=https://pocitadla.services-support.cz
Email__DryRun=false
Email__OverrideRecipient=
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://+:8080
```

### AWS SES prerekvizity

1. IAM uživatel s oprávněním `ses:SendEmail` na odesílací identitu
2. Ověřená doména nebo adresa v SES konzoli
3. SES produkční přístup (sandbox omezuje příjemce pouze na ověřené adresy)
4. Region: `eu-central-1` (Frankfurt)

---

## 13. Testování emailů

### Tři módy — kombinovatelné

**1. Dry run** — žádné volání SES, pouze log
```json
"Email": { "DryRun": true }
```

**2. Přesměrování na testovací inbox** — reálné odeslání přes SES, ale jiný příjemce
```json
"Email": { "OverrideRecipient": "developer@firma.cz" }
```

**3. Manuální trigger** — spustí job okamžitě bez čekání na 28.
```http
POST /api/admin/emails/trigger
X-Api-Key: <klíč>
```

### Doporučený postup testování

1. `DryRun: true` + trigger → ověřit v logu správné příjemce a počty zařízení
2. `OverrideRecipient: "vy@..."` + trigger → zkontrolovat HTML email v inboxu
3. Obojí vypnout v produkčním `.env` → ostrý provoz

---

## 14. Provozní postupy

### Sledování stavu odevzdání

```http
GET /api/admin/status
X-Api-Key: <klíč>
```

### Opětovné odeslání emailu konkrétnímu příjemci

```http
POST /api/admin/emails/resend/zakaznik@firma.cz
X-Api-Key: <klíč>
```

### Ruční spuštění emailového kola

```http
POST /api/admin/emails/trigger
X-Api-Key: <klíč>
```

### Zobrazení logů kontejneru

```bash
docker logs sesu-api --tail 100 -f
```

### Restart aplikace

```bash
docker compose -f /opt/docker/apps/sesu-api/docker-compose.yml restart sesu-api
```

### Vynucení aktualizace na nejnovější verzi

```bash
cd /opt/docker/apps/sesu-api
docker compose pull
docker compose up -d
```
