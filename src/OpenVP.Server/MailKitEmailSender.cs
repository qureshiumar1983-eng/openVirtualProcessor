using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using OpenVP.Core.Alerts;

namespace OpenVP.Server;

/// <summary>
/// Sends alert emails over SMTP with <see href="https://github.com/jstedfast/MailKit">MailKit</see> (MIT): STARTTLS (the default, usually port 587),
/// implicit TLS (<c>ssl</c>, usually 465) or no encryption, with an optional user name and password (the password comes from the secret store).
/// Each message has a plain-text part and an HTML part.
/// </summary>
public sealed class MailKitEmailSender : IEmailSender
{
    public static MimeMessage Build(EmailMessage m)
    {
        var msg = new MimeMessage();
        msg.From.Add(MailboxAddress.Parse(m.From));
        foreach (var to in m.To) msg.To.Add(MailboxAddress.Parse(to));
        msg.Subject = m.Subject;
        msg.Headers.Add("X-Mailer", "OpenVP");
        msg.Headers.Add("Auto-Submitted", "auto-generated");
        msg.Body = new BodyBuilder { TextBody = m.Text, HtmlBody = m.Html }.ToMessageBody();
        return msg;
    }

    public async Task SendAsync(SmtpEndpoint server, EmailMessage message, CancellationToken ct)
    {
        using var client = new SmtpClient();
        var options = server.Security switch { "ssl" => SecureSocketOptions.SslOnConnect, "none" => SecureSocketOptions.None, _ => SecureSocketOptions.StartTls };
        await client.ConnectAsync(server.Host, server.Port, options, ct);
        if (!string.IsNullOrEmpty(server.Username))
            await client.AuthenticateAsync(server.Username, server.Password ?? "", ct);
        await client.SendAsync(Build(message), ct);
        await client.DisconnectAsync(true, ct);
    }
}
