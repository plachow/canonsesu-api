using LinqToDB.Mapping;

namespace CanonSeSu.Api.Data.Models;

[Table("service_device_counters", Schema = "public")]
public class ServiceDeviceCounter
{
    [PrimaryKey, Identity]
    [Column("recordid")]
    public int RecordId { get; set; }
    
    [Column("email")]
    public string? Email { get; set; }

    [Column("idcode")]
    public string? IdCode { get; set; }

    [Column("typkonfigurace")]
    public string? TypKonfigurace { get; set; }

    [Column("typstroje")]
    public string? TypStroje { get; set; }

    [Column("vyrobnicislo")]
    public string? VyrobniCislo { get; set; }

    [Column("typpocitadla")]
    public string? TypPocitadla { get; set; }

    [Column("nazevpocitadla")]
    public string? NazevPocitadla { get; set; }

    [Column("datumposlednihohlaseni")]
    public DateTime? DatumPoslednihoHlaseni { get; set; }

    [Column("poslednistavpocitadla")]
    public int? PosledniStavPocitadla { get; set; }

    [Column("datumaktualnihohlaseni")]
    public DateTime? DatumAktualnihoHlaseni { get; set; }

    [Column("deadlinedate")]
    public DateTime? DeadlineDate { get; set; }

    [Column("aktualnistavpocitadla")]
    public int? AktualniStavPocitadla { get; set; }

    [Column("poznamka")]
    public string? Poznamka { get; set; }

    [Column("datumcasnahlaseni")]
    public DateTime? DatumCasNahlaseni { get; set; }
}
