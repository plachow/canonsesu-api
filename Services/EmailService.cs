using Amazon;
using Amazon.Runtime;
using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using CanonSeSu.Api.Data;
using CanonSeSu.Api.Data.Models;
using LinqToDB;

namespace CanonSeSu.Api.Services;

public class EmailService(IConfiguration configuration, ILogger<EmailService> logger, AppDb db)
{
    private readonly EmailSettings _settings = configuration
        .GetSection("Email")
        .Get<EmailSettings>() ?? throw new InvalidOperationException("Email configuration is missing.");

    private AmazonSimpleEmailServiceV2Client CreateSesClient()
    {
        var aws = configuration.GetSection("Aws");
        var credentials = new BasicAWSCredentials(
            aws["AccessKeyId"] ?? throw new InvalidOperationException("Aws:AccessKeyId is missing."),
            aws["SecretAccessKey"] ?? throw new InvalidOperationException("Aws:SecretAccessKey is missing."));
        var region = RegionEndpoint.GetBySystemName(aws["Region"] ?? "eu-central-1");
        return new AmazonSimpleEmailServiceV2Client(credentials, region);
    }

    public async Task SendCounterRequestEmailsAsync(
        IEnumerable<IGrouping<string, ServiceDeviceCounter>> devicesByEmail,
        CancellationToken cancellationToken = default)
    {
        var isDryRun = _settings.DryRun;
        var overrideRecipient = _settings.OverrideRecipient;

        if (isDryRun)
            logger.LogWarning("DRY RUN mode — no emails will be sent to SES.");
        else if (!string.IsNullOrWhiteSpace(overrideRecipient))
            logger.LogWarning("OverrideRecipient is set — all emails will be redirected to {Override}.", overrideRecipient);

        using var client = isDryRun ? null : CreateSesClient();
        var period = DateTime.Now;
        int sent = 0, failed = 0;

        foreach (var group in devicesByEmail)
        {
            var originalEmail = group.Key;
            var devices = group.ToList();
            var idCode = devices.First().IdCode!;
            var deadline = devices.First().DeadlineDate;
            var recipient = !string.IsNullOrWhiteSpace(overrideRecipient) ? overrideRecipient : originalEmail;

            try
            {
                var html = BuildHtmlBody(devices, idCode, deadline, period);
                var text = BuildTextBody(devices, idCode, period);

                if (isDryRun)
                {
                    logger.LogInformation(
                        "[DRY RUN] Would send to {OriginalEmail} ({DeviceCount} devices, idcode={IdCode})",
                        originalEmail, devices.Count, idCode);
                    await MarkEmailSentAsync(devices, cancellationToken);
                    sent++;
                    continue;
                }

                var request = new SendEmailRequest
                {
                    FromEmailAddress = $"{_settings.FromName} <{_settings.FromAddress}>",
                    Destination = new Destination { ToAddresses = [recipient] },
                    ReplyToAddresses = [_settings.ReplyToAddress ?? _settings.FromAddress],
                    Content = new EmailContent
                    {
                        Simple = new Message
                        {
                            Subject = new Content
                            {
                                Charset = "UTF-8",
                                Data = string.IsNullOrWhiteSpace(overrideRecipient)
                                    ? $"Hlášení stavu počítadel – {period:MMMM yyyy}"
                                    : $"[TEST: {originalEmail}] Hlášení stavu počítadel – {period:MMMM yyyy}"
                            },
                            Body = new Body
                            {
                                Html = new Content { Charset = "UTF-8", Data = html },
                                Text = new Content { Charset = "UTF-8", Data = text }
                            }
                        }
                    }
                };

                await client!.SendEmailAsync(request, cancellationToken);
                await MarkEmailSentAsync(devices, cancellationToken);
                sent++;
                logger.LogInformation("Email sent → {Recipient} (original: {OriginalEmail}, {DeviceCount} devices)",
                    recipient, originalEmail, devices.Count);
            }
            catch (Exception ex)
            {
                failed++;
                logger.LogError(ex, "Failed to send email to {Email}", originalEmail);
            }
        }

        logger.LogInformation("Email batch complete. Sent: {Sent}, Failed: {Failed}{DryRun}",
            sent, failed, isDryRun ? " [DRY RUN]" : string.Empty);
    }

    private async Task MarkEmailSentAsync(List<ServiceDeviceCounter> devices, CancellationToken cancellationToken)
    {
        var ids = devices.Select(d => d.RecordId).ToList();
        var now = DateTime.UtcNow;
        await db.ServiceDeviceCounters
            .Where(x => ids.Contains(x.RecordId))
            .Set(x => x.EmailSentDate, now)
            .UpdateAsync(cancellationToken);
    }

    private string BuildHtmlBody(
        List<ServiceDeviceCounter> devices,
        string idCode,
        DateTime? deadline,
        DateTime period)
    {
        var portalUrl = $"{_settings.UserPortalBaseUrl}?idcode={idCode}";
        var deadlineStr = deadline.HasValue
            ? deadline.Value.ToString("d. M. yyyy")
            : "konce tohoto měsíce";
        var periodStr = period.ToString("MMMM yyyy");

        var deviceRows = string.Concat(devices.Select((d, i) => $"""
            <tr style="background-color:{(i % 2 == 0 ? "#ffffff" : "#f9f9f9")};">
              <td style="padding:12px 16px;border-bottom:1px solid #e8e8e8;font-size:14px;color:#333333;">{Encode(d.TypStroje)}</td>
              <td style="padding:12px 16px;border-bottom:1px solid #e8e8e8;font-size:14px;color:#333333;font-family:monospace;">{Encode(d.VyrobniCislo)}</td>
              <td style="padding:12px 16px;border-bottom:1px solid #e8e8e8;font-size:14px;color:#333333;">{Encode(d.NazevPocitadla)}</td>
              <td style="padding:12px 16px;border-bottom:1px solid #e8e8e8;font-size:14px;color:#aaaaaa;text-align:center;">–</td>
            </tr>
            """));

        return $"""
            <!DOCTYPE html>
            <html lang="cs">
            <head>
              <meta charset="UTF-8" />
              <meta name="viewport" content="width=device-width, initial-scale=1.0" />
              <meta http-equiv="X-UA-Compatible" content="IE=edge" />
              <title>Hlášení stavu počítadel – {periodStr}</title>
            </head>
            <body style="margin:0;padding:0;background-color:#f4f4f4;font-family:Arial,Helvetica,sans-serif;">
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background-color:#f4f4f4;padding:32px 16px;">
                <tr>
                  <td align="center">
                    <table role="presentation" width="600" cellpadding="0" cellspacing="0" style="max-width:600px;width:100%;background-color:#ffffff;border-radius:6px;overflow:hidden;box-shadow:0 2px 8px rgba(0,0,0,0.08);">

                      <!-- Header -->
                      <tr>
                        <td style="background-color:#cc0000;padding:28px 32px;">
                          <table role="presentation" width="100%" cellpadding="0" cellspacing="0">
                            <tr>
                              <td>
                                <span style="font-size:22px;font-weight:bold;color:#ffffff;letter-spacing:1px;">CANON</span>
                                <span style="font-size:13px;color:#ffcccc;margin-left:8px;">CZ s.r.o.</span>
                              </td>
                              <td align="right">
                                <span style="font-size:12px;color:#ffcccc;">Servisní portál</span>
                              </td>
                            </tr>
                          </table>
                        </td>
                      </tr>

                      <!-- Title bar -->
                      <tr>
                        <td style="background-color:#b30000;padding:10px 32px;">
                          <span style="font-size:13px;color:#ffaaaa;text-transform:uppercase;letter-spacing:0.5px;">Hlášení stavu počítadel – {periodStr}</span>
                        </td>
                      </tr>

                      <!-- Body -->
                      <tr>
                        <td style="padding:32px 32px 24px;">
                          <p style="margin:0 0 16px;font-size:15px;color:#333333;line-height:1.6;">Vážený zákazníku,</p>
                          <p style="margin:0 0 16px;font-size:15px;color:#333333;line-height:1.6;">
                            prosíme o nahlášení stavu počítadel k poslednímu dni tohoto kalendářního měsíce
                            pro stroje uvedené v tabulce níže. Termín pro odeslání hlášení je <strong>{deadlineStr}</strong>.
                          </p>
                          <p style="margin:0 0 24px;font-size:15px;color:#333333;line-height:1.6;">
                            Kliknutím na tlačítko níže otevřete formulář pro všechna Vaše zařízení najednou.
                          </p>

                          <!-- CTA Button -->
                          <table role="presentation" cellpadding="0" cellspacing="0" style="margin-bottom:32px;">
                            <tr>
                              <td style="border-radius:4px;background-color:#cc0000;">
                                <a href="{portalUrl}" target="_blank"
                                   style="display:inline-block;padding:14px 32px;font-size:15px;font-weight:bold;color:#ffffff;text-decoration:none;border-radius:4px;letter-spacing:0.3px;">
                                  Nahlásit stav počítadel &rsaquo;
                                </a>
                              </td>
                            </tr>
                          </table>

                          <!-- Device table -->
                          <p style="margin:0 0 12px;font-size:13px;font-weight:bold;color:#666666;text-transform:uppercase;letter-spacing:0.5px;">Přehled zařízení</p>
                          <table role="presentation" width="100%" cellpadding="0" cellspacing="0"
                                 style="border:1px solid #e8e8e8;border-radius:4px;border-collapse:collapse;overflow:hidden;">
                            <thead>
                              <tr style="background-color:#f0f0f0;">
                                <th style="padding:10px 16px;text-align:left;font-size:12px;color:#666666;font-weight:bold;text-transform:uppercase;letter-spacing:0.4px;border-bottom:2px solid #e0e0e0;">Typ stroje</th>
                                <th style="padding:10px 16px;text-align:left;font-size:12px;color:#666666;font-weight:bold;text-transform:uppercase;letter-spacing:0.4px;border-bottom:2px solid #e0e0e0;">Sériové číslo</th>
                                <th style="padding:10px 16px;text-align:left;font-size:12px;color:#666666;font-weight:bold;text-transform:uppercase;letter-spacing:0.4px;border-bottom:2px solid #e0e0e0;">Název počítadla</th>
                                <th style="padding:10px 16px;text-align:center;font-size:12px;color:#666666;font-weight:bold;text-transform:uppercase;letter-spacing:0.4px;border-bottom:2px solid #e0e0e0;">Stav počítadla</th>
                              </tr>
                            </thead>
                            <tbody>
                              {deviceRows}
                            </tbody>
                          </table>
                        </td>
                      </tr>

                      <!-- Info box -->
                      <tr>
                        <td style="padding:0 32px 32px;">
                          <table role="presentation" width="100%" cellpadding="0" cellspacing="0"
                                 style="background-color:#fff8e1;border-left:4px solid #f5a623;border-radius:0 4px 4px 0;padding:16px;">
                            <tr>
                              <td style="padding:16px;">
                                <p style="margin:0 0 8px;font-size:13px;font-weight:bold;color:#7a5700;">Výpočet počítadel Total BW / Total Colour</p>
                                <p style="margin:0 0 4px;font-size:13px;color:#5a4000;line-height:1.5;">
                                  Pokud Váš stroj neobsahuje počítadla Total BW a Total Colour, použijte prosím:
                                </p>
                                <p style="margin:4px 0;font-size:13px;color:#5a4000;font-family:monospace;">Total BW = 2 × 112 (Black/Large) + 113 (Black/Small)</p>
                                <p style="margin:4px 0;font-size:13px;color:#5a4000;font-family:monospace;">Total Colour = 2 × 122 (Color/Large) + 123 (Color/Small)</p>
                              </td>
                            </tr>
                          </table>
                        </td>
                      </tr>

                      <!-- Footer -->
                      <tr>
                        <td style="background-color:#f4f4f4;padding:24px 32px;border-top:1px solid #e8e8e8;">
                          <p style="margin:0 0 8px;font-size:13px;color:#999999;line-height:1.5;">
                            Děkujeme za spolupráci.<br />
                            <strong style="color:#666666;">Canon CZ s.r.o.</strong> — Servisní oddělení
                          </p>
                          <p style="margin:8px 0 0;font-size:11px;color:#bbbbbb;line-height:1.5;">
                            Tento e-mail byl odeslán automaticky. Neodpovídejte na tuto zprávu.
                            Pokud máte dotazy, kontaktujte nás na <a href="mailto:{_settings.FromAddress}" style="color:#cc0000;text-decoration:none;">{_settings.FromAddress}</a>.
                          </p>
                          <p style="margin:8px 0 0;font-size:11px;color:#cccccc;">
                            Odkaz pro přístup k hlášení: <a href="{portalUrl}" style="color:#cc0000;word-break:break-all;">{portalUrl}</a>
                          </p>
                        </td>
                      </tr>

                    </table>
                  </td>
                </tr>
              </table>
            </body>
            </html>
            """;
    }

    private string BuildTextBody(List<ServiceDeviceCounter> devices, string idCode, DateTime period)
    {
        var portalUrl = $"{_settings.UserPortalBaseUrl}?idcode={idCode}";
        var lines = new System.Text.StringBuilder();
        lines.AppendLine($"Hlášení stavu počítadel – {period:MMMM yyyy}");
        lines.AppendLine(new string('=', 50));
        lines.AppendLine();
        lines.AppendLine("Vážený zákazníku,");
        lines.AppendLine();
        lines.AppendLine("prosíme o nahlášení stavu počítadel k poslednímu dni tohoto kalendářního měsíce.");
        lines.AppendLine();
        lines.AppendLine($"Formulář pro hlášení: {portalUrl}");
        lines.AppendLine();
        lines.AppendLine("Přehled zařízení:");
        lines.AppendLine(new string('-', 50));
        foreach (var d in devices)
            lines.AppendLine($"  {d.TypStroje} | {d.VyrobniCislo} | {d.NazevPocitadla}");
        lines.AppendLine();
        lines.AppendLine("Výpočet počítadel (pokud stroj neobsahuje Total BW / Total Colour):");
        lines.AppendLine("  Total BW     = 2 * 112 (Black/Large) + 113 (Black/Small)");
        lines.AppendLine("  Total Colour = 2 * 122 (Color/Large) + 123 (Color/Small)");
        lines.AppendLine();
        lines.AppendLine("Děkujeme za spolupráci,");
        lines.AppendLine("Canon CZ s.r.o. – Servisní oddělení");
        return lines.ToString();
    }

    private static string Encode(string? value) =>
        System.Net.WebUtility.HtmlEncode(value ?? string.Empty);
}

public class EmailSettings
{
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = string.Empty;
    public string? ReplyToAddress { get; set; }
    public string UserPortalBaseUrl { get; set; } = string.Empty;
    /// <summary>When true, emails are logged but never sent to SES.</summary>
    public bool DryRun { get; set; } = false;
    /// <summary>When set, all emails are redirected to this address instead of real recipients.</summary>
    public string? OverrideRecipient { get; set; }
}
