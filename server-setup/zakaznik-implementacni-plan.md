# Systém hlášení počítadel — Průvodce správou

Tento dokument popisuje, jak systém funguje a jak s ním pracovat.

---

## Jak systém funguje

Každý měsíc proběhnou automaticky tři kroky:

1. **Vy nahrajete seznam aktivních tiskáren** (jednou za měsíc, ideálně 1. dne)
2. **Systém 28. v měsíci automaticky rozešle emaily** — každý zákazník dostane odkaz na svůj formulář
3. **Zákazníci vyplní aktuální stav počítadel** přes webový formulář a odešlou

Váš jediný pravidelný úkon je tedy nahrání tiskáren. Vše ostatní systém obstará sám.

---

## Pravidla pro zákazníky

- Každý zákazník dostane **jeden email s jedním odkazem** — odkaz zobrazí všechna jeho zařízení najednou
- Zákazník zadává stav počítadel **k poslednímu dni příslušného měsíce**
- Zadaná hodnota musí být **vyšší nebo stejná** jako v předchozím hlášení — systém nižší hodnotu odmítne
- Pokud zákazník udělá chybu, může formulář otevřít znovu a hodnoty opravit — **až do termínu odevzdání**
- Po uplynutí termínu systém hlášení zablokuje — zákazník musí kontaktovat servisní oddělení

### Výpočet počítadel Total BW / Total Colour

Pokud tiskárna nemá přímo počítadla **Total BW** a **Total Colour**, zákazník použije tato dílčí:

```
Total BW     = 2 × 112 (Black/Large) + 113 (Black/Small)
Total Colour = 2 × 122 (Color/Large) + 123 (Color/Small)
```

Hodnoty 112, 113, 122, 123 najde zákazník v menu tiskárny v sekci počítadel.

---

## Měsíční nahrání tiskáren

Každý měsíc je nutné systému předat aktuální seznam aktivních tiskáren. Doporučujeme to nastavit automaticky přes SQL Server Agent — viz kapitola [Automatizace](#automatizace-přes-sql-server-agent--powershell) níže.

**Endpoint pro nahrání:**

```
POST https://pocitadla.services-support.cz/api/devices/bulk
X-Api-Key: <váš API klíč>
```

Odesíláte JSON seznam — jeden řádek = jedno počítadlo jednoho stroje:

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

| Pole | Co sem patří |
|---|---|
| `email` | Email zákazníka, kterému přijde výzva k hlášení |
| `typKonfigurace` | Interní kód konfigurace stroje |
| `typStroje` | Obchodní název stroje |
| `vyrobniCislo` | Sériové číslo stroje |
| `typPocitadla` | Kód počítadla |
| `nazevPocitadla` | Název počítadla |
| `datumPoslednihoHlaseni` | Datum předchozího hlášení |
| `posledniStavPocitadla` | Hodnota počítadla z minulého měsíce |
| `datumAktualnihoHlaseni` | Datum aktuálního období (zpravidla 1. den měsíce) |
| `deadlineDate` | Termín, do kdy musí zákazník hlášení odeslat |

Pole `idcode` **nevyplňujte** — systém ho vygeneruje sám. Všechna zařízení se stejným emailem dostanou automaticky stejný kód (= jeden odkaz pro zákazníka).

**Odpověď systému** vrátí vygenerované kódy:

```json
[
  { "email": "zakaznik@firma.cz", "idCode": "a3f9c2d1b8e74f56..." }
]
```

---

## Přehled správcovských funkcí

Všechny níže uvedené funkce vyžadují hlavičku `X-Api-Key: <váš API klíč>`.

---

### Ověření dostupnosti systému

Chcete zkontrolovat, zda systém běží?

```
GET https://pocitadla.services-support.cz/health
```

Vrátí `Healthy` při normálním provozu. Nevyžaduje API klíč. Vhodné pro monitoring.

---

### Aktuální období a termín

Zjistíte aktuální hlásicí období a datum termínu bez přihlášení:

```
GET https://pocitadla.services-support.cz/api/info
```

Odpověď:

```json
{
  "period": "2026-03-01",
  "deadline": "2026-03-28",
  "isPastDeadline": false
}
```

---

### Zobrazit nahrané tiskárny

Chcete zkontrolovat, co je aktuálně v systému nahráno?

```
GET https://pocitadla.services-support.cz/api/devices/current
X-Api-Key: <váš API klíč>
```

Bez dalších parametrů vrátí tiskárny pro **aktuální (nejnovější) období**. Potřebujete-li zobrazit jiné období, přidejte filtr:

```
GET https://pocitadla.services-support.cz/api/devices/current?startDate=2026-02-01&endDate=2026-02-28
```

---

### Zkontrolovat stav odevzdání

Kolik zákazníků už hlášení odeslalo a kolik ještě ne?

```
GET https://pocitadla.services-support.cz/api/admin/status
X-Api-Key: <váš API klíč>
```

Odpověď:

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

---

### Rozeslat emaily ručně

Emaily se odesílají automaticky 28. v měsíci. Pokud potřebujete rozeslání spustit dříve (například pro ověření nebo po opravě dat), použijte:

```
POST https://pocitadla.services-support.cz/api/admin/emails/trigger
X-Api-Key: <váš API klíč>
```

Systém odešle emaily všem, kteří ještě žádný nedostali. Zákazníci, kterým email již přišel, jsou přeskočeni.

---

### Znovu poslat email jednomu zákazníkovi

Zákazník volá, že email nedostal? Pošlete mu ho znovu:

```
POST https://pocitadla.services-support.cz/api/admin/emails/resend/zakaznik@firma.cz
X-Api-Key: <váš API klíč>
```

Nahraďte `zakaznik@firma.cz` skutečnou emailovou adresou zákazníka. Systém pošle email bez ohledu na to, zda ho zákazník již dříve dostal.

---

## Automatizace přes SQL Server Agent + PowerShell

Doporučujeme nastavit automatické měsíční nahrání přímo z vašeho SQL Serveru, aby vám na nic nezáleželo.

### Krok 1 — Připravte PowerShell skript

Uložte jako `C:\Scripts\UploadPrinters.ps1`:

```powershell
$apiUrl    = "https://pocitadla.services-support.cz/api/devices/bulk"
$apiKey    = "VAS_API_KLIC"
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
     - **Frequency:** Monthly, 1. den v měsíci, 06:00
4. Uložte job

### Krok 3 — Ověřte první spuštění

```powershell
& "C:\Scripts\UploadPrinters.ps1"
```

Výstupní CSV by měl obsahovat vygenerované kódy pro každý email.

---

## Harmonogram

| Krok | Kdy | Poznámka |
|---|---|---|
| Nahrání tiskáren | 1. den v měsíci, 06:00 | SQL Agent Job (automaticky) |
| Odeslání emailů | 28. den v měsíci, 02:00 | Systém (automaticky) |
| Termín odevzdání | Dle `deadlineDate` | Zákazník vyplní formulář |
| Kontrola odevzdání | Kdykoli | `GET /api/admin/status` |

---

## Kontakt a podpora

**Canon CZ s.r.o.**
E-mail: pocitadla-cz@canon.cz
