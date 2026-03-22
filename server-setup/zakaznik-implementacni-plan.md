# Implementační plán — Zákazník Canon CZ

Postup nastavení automatického měsíčního přenosu dat o tiskárnách do systému hlášení počítadel.

---

## Přehled procesu

Každý měsíc je potřeba do systému nahrát seznam aktivních tiskáren spolu s hodnotami počítadel z předchozího měsíce. Na základě těchto dat systém:

1. Vygeneruje přístupové kódy pro jednotlivé příjemce
2. Automaticky odešle 28. v měsíci emaily s výzvou k hlášení
3. Příjemci vyplní aktuální stav počítadel přes webový formulář

---

## Pravidla hlášení počítadel

### Kdy se hlášení otevírá

- Email s výzvou k hlášení je odesílán automaticky **28. každého měsíce** (zpravidla se jedná o výzvu k hlášení za daný měsíc k jeho poslednímu dni)
- Hlášení je aktivní od doručení emailu do data `deadlineDate`, které je součástí každého záznamu

### Pravidla pro vyplnění

- **Aktuální stav počítadla musí být ≥ hodnotě z předchozího hlášení** — systém zamítne nižší hodnotu
- Zákazník zadává stav počítadel k **poslednímu dni příslušného měsíce**
- Každý příjemce (email) dostane jeden odkaz pokrývající **všechna jeho zařízení najednou**
- Oprava chybně zadaného hlášení je možná — zákazník může stejný odkaz použít opakovaně a hodnoty přepsat, pokud termín ještě neuplynul
- Po uplynutí termínu (`deadlineDate`) systém **odmítne nové odeslání** — v takovém případě je nutné kontaktovat servisní oddělení

### Výpočet počítadel Total BW / Total Colour

Pokud stroj neobsahuje přímo počítadla **Total BW** a **Total Colour**, je nutné použít tato dílčí počítadla:

```
Total BW     = 2 × 112 (Black/Large) + 113 (Black/Small)
Total Colour = 2 × 122 (Color/Large) + 123 (Color/Small)
```

Hodnoty počítadel 112, 113, 122, 123 jsou k nalezení v menu tiskárny v sekci počítadel.

---

## Technický postup nastavení přenosu dat

### Co API očekává

Každý měsíc je nutné zavolat endpoint:

```
POST https://api.services-support.cz/api/devices/bulk
X-Api-Key: <váš API klíč>
Content-Type: application/json
```

Tělo požadavku je JSON pole, kde každý objekt reprezentuje **jedno počítadlo jednoho stroje**:

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

#### Popis polí

| Pole | Popis | Příklad |
|---|---|---|
| `email` | Email příjemce — dostane odkaz k hlášení | `zakaznik@firma.cz` |
| `typKonfigurace` | Interní kód konfigurace stroje | `V700` |
| `typStroje` | Obchodní název stroje | `IMAGEPRESS V700 SERIES` |
| `vyrobniCislo` | Sériové číslo stroje | `4VB05026` |
| `typPocitadla` | Kód počítadla | `122` |
| `nazevPocitadla` | Čitelný název počítadla | `Color/Large (E)` |
| `datumPoslednihoHlaseni` | Datum předchozího hlášení | `2026-02-01` |
| `posledniStavPocitadla` | Hodnota počítadla z minulého měsíce | `12345` |
| `datumAktualnihoHlaseni` | Datum aktuálního období (zpravidla 1. den měsíce) | `2026-03-01` |
| `deadlineDate` | Termín odevzdání hlášení | `2026-03-28` |

> **Poznámka:** Pole `idcode` se **nevkládá** — systém ho vygeneruje automaticky. Jeden kód je přiřazen všem zařízením se stejným emailem.

#### Odpověď API

API vrátí přiřazené přístupové kódy — jeden per unikátní email:

```json
[
  { "email": "zakaznik@firma.cz", "idCode": "a3f9c2d1b8e74f56..." }
]
```

---

## Automatizace přes SQL Server Agent + PowerShell

Doporučený způsob automatického měsíčního přenosu dat z vašeho SQL Serveru.

### Krok 1 — Připravte PowerShell skript

Uložte jako `C:\Scripts\UploadPrinters.ps1`:

```powershell
$apiUrl  = "https://api.services-support.cz/api/devices/bulk"
$apiKey  = "VAS_API_KLIC"
$sqlServer = "VAS_SQL_SERVER"
$sqlDb     = "VASE_DATABAZE"

# Upravte dotaz dle vaší databázové struktury
$query = @"
SELECT
    email                          AS email,
    typ_konfigurace                AS typKonfigurace,
    typ_stroje                     AS typStroje,
    vyrobni_cislo                  AS vyrobniCislo,
    typ_pocitadla                  AS typPocitadla,
    nazev_pocitadla                AS nazevPocitadla,
    datum_posledniho_hlaseni       AS datumPoslednihoHlaseni,
    posledni_stav_pocitadla        AS posledniStavPocitadla,
    DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1)  AS datumAktualnihoHlaseni,
    DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 28) AS deadlineDate
FROM vw_aktivni_tiskarny_mesic
"@

$rows = Invoke-Sqlcmd -Query $query -ServerInstance $sqlServer -Database $sqlDb

$json = $rows | ForEach-Object {
    [ordered]@{
        email                  = $_.email
        typKonfigurace         = $_.typKonfigurace
        typStroje              = $_.typStroje
        vyrobniCislo           = $_.vyrobniCislo
        typPocitadla           = $_.typPocitadla
        nazevPocitadla         = $_.nazevPocitadla
        datumPoslednihoHlaseni = $_.datumPoslednihoHlaseni.ToString("yyyy-MM-dd")
        posledniStavPocitadla  = [int]$_.posledniStavPocitadla
        datumAktualnihoHlaseni = $_.datumAktualnihoHlaseni.ToString("yyyy-MM-dd")
        deadlineDate           = $_.deadlineDate.ToString("yyyy-MM-dd")
    }
} | ConvertTo-Json -AsArray

$response = Invoke-RestMethod `
    -Uri $apiUrl `
    -Method POST `
    -Body $json `
    -ContentType "application/json" `
    -Headers @{ "X-Api-Key" = $apiKey }

# Uložit přiřazené kódy pro evidenci
$outputFile = "C:\Scripts\idcodes_$(Get-Date -Format 'yyyy-MM').csv"
$response | Export-Csv $outputFile -NoTypeInformation -Encoding UTF8

Write-Host "Nahráno $($response.Count) záznamů. Kódy uloženy: $outputFile"
```

### Krok 2 — Nastavte SQL Server Agent Job

1. Otevřete **SQL Server Management Studio**
2. Rozbalte **SQL Server Agent** → klikněte pravým tlačítkem na **Jobs** → **New Job**
3. Vyplňte:
   - **Name:** `Mesicni upload tiskaren Canon API`
   - **Steps** → New Step:
     - **Type:** `PowerShell`
     - **Command:** `& "C:\Scripts\UploadPrinters.ps1"`
   - **Schedules** → New Schedule:
     - **Frequency:** Monthly
     - **Day:** 1 (první den v měsíci)
     - **Time:** 06:00:00
4. Uložte job

### Krok 3 — Ověřte první spuštění

Po prvním automatickém nebo ručním spuštění zkontrolujte:

```powershell
# Ruční spuštění pro test
& "C:\Scripts\UploadPrinters.ps1"
```

Výstupní CSV soubor by měl obsahovat vygenerované idkódy pro každý email. Případné chyby jsou vypsány do konzole nebo logu SQL Agent Jobu.

---

## Ověření přenosu

Po nahraní dat do systému si můžete stav ověřit přes API:

```http
GET https://api.services-support.cz/api/admin/status
X-Api-Key: <váš API klíč>
```

Odpověď ukáže počty zařízení, příjemců a procento odevzdání pro aktuální období.

---

## Ruční odeslání emailů

Emaily jsou odesílány automaticky 28. v měsíci. Pokud potřebujete odeslání spustit dříve (například pro ověření):

```http
POST https://api.services-support.cz/api/admin/emails/trigger
X-Api-Key: <váš API klíč>
```

Opakované odeslání emailu jednomu příjemci:

```http
POST https://api.services-support.cz/api/admin/emails/resend/zakaznik@firma.cz
X-Api-Key: <váš API klíč>
```

---

## Přehled harmonogramu

| Událost | Kdy | Kdo / Co |
|---|---|---|
| Nahrání tiskáren do systému | 1. den v měsíci, 06:00 | SQL Server Agent Job (automaticky) |
| Odeslání emailů s výzvou | 28. den v měsíci, 02:00 | Systém (automaticky) |
| Termín odevzdání hlášení | Dle `deadlineDate` v záznamu | Zákazník vyplní formulář |
| Kontrola odevzdání | Kdykoli | `GET /api/admin/status` |

---

## Kontakt a podpora

V případě technických dotazů nebo problémů s přenosem dat kontaktujte:

**Canon CZ s.r.o. — Servisní oddělení**
E-mail: servis@services-support.cz
