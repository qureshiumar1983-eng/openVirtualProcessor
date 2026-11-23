using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using OpenVP.Core;
using OpenVP.Server;

namespace OpenVP.Tests;

/// <summary>A real OpenVP server on a free port in this process, with its own data folder, and a client that sends its access key.</summary>
public sealed class OpsTestServer : IDisposable
{
    public WebApplication App;
    public int Port;
    public string Dir, Key, Url;
    public Processor P;
    public HttpClient Http;

    public OpsTestServer(bool localRequiresKey = true, string scheme = "http", string? pfx = null, string? pfxPassword = null, string? existingData = null, params string[] more)
    {
        Dir = existingData ?? Directory.CreateTempSubdirectory("openvp-ops-e2e").FullName;
        Port = IntegrationServiceTests.FreePort();
        Url = $"{scheme}://127.0.0.1:{Port}";
        var args = new List<string> { $"--urls={Url}", "--OpenVP:TunnelPort=0", $"--OpenVP:DataDirectory={Dir}", "--OpenVP:CipEnabled=false", "--OpenVP:UnsupportedLog=off", "--OpenVP:Slots=3", "--OpenVP:Mdns=false", $"--OpenVP:LocalRequiresKey={localRequiresKey}" };
        if (pfx != null) { args.Add($"--Kestrel:Certificates:Default:Path={pfx}"); args.Add($"--Kestrel:Certificates:Default:Password={pfxPassword}"); }
        args.AddRange(more);
        App = OpenVpServer.Create(args.ToArray());
        App.StartAsync().GetAwaiter().GetResult();
        Key = File.Exists(Path.Combine(Dir, "apikey.txt")) ? File.ReadAllText(Path.Combine(Dir, "apikey.txt")).Trim() : ""; // no file when the key is off
        P = App.Services.GetRequiredService<Processor>();
        Http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, SslOptions = { RemoteCertificateValidationCallback = (_, _, _, _) => true } }) { Timeout = TimeSpan.FromSeconds(60) };
        Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Key);
    }

    public (int Status, JsonElement Json, string Text) Call(HttpMethod m, string path, object? body = null)
    {
        var req = new HttpRequestMessage(m, Url + path);
        if (body != null) req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        var r = Http.Send(req);
        var text = r.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        JsonElement j = default; try { if (text.Length > 0) j = JsonDocument.Parse(text).RootElement.Clone(); } catch (JsonException) { }
        return ((int)r.StatusCode, j, text);
    }

    public (int Status, JsonElement Json, string Text) Get(string path) => Call(HttpMethod.Get, path);
    public (int Status, JsonElement Json, string Text) Post(string path, object? body = null) => Call(HttpMethod.Post, path, body ?? new { });
    public (int Status, JsonElement Json, string Text) Put(string path, object body) => Call(HttpMethod.Put, path, body);
    public (int Status, JsonElement Json, string Text) Delete(string path) => Call(HttpMethod.Delete, path);

    public (int Status, JsonElement Json, string Text) Upload(string path, string file, string? name = null, params (string, string)[] fields)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(File.ReadAllBytes(file)), "file", name ?? Path.GetFileName(file));
        foreach (var (k, v) in fields) form.Add(new StringContent(v), k);
        var r = Http.PostAsync(Url + path, form).GetAwaiter().GetResult();
        var text = r.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        JsonElement j = default; try { if (text.Length > 0) j = JsonDocument.Parse(text).RootElement.Clone(); } catch (JsonException) { }
        return ((int)r.StatusCode, j, text);
    }

    /// <summary>Loads the SIMPL Windows sample into a slot through the API.</summary>
    public void LoadSample(int slot, string name = "SampleProgram.smw", string? smwText = null)
    {
        var dir = Path.Combine(Paths.RepoRoot, "samples", "SimplWindows");
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(smwText != null ? Encoding.Latin1.GetBytes(smwText) : File.ReadAllBytes(Path.Combine(dir, "SampleProgram.smw"))), "file", name);
        form.Add(new ByteArrayContent(File.ReadAllBytes(Path.Combine(dir, "SampleProgram.openvp.json"))), "file", Path.ChangeExtension(name, ".openvp.json"));
        var r = Http.PostAsync($"{Url}/api/slots/{slot}/program", form).GetAwaiter().GetResult();
        if (!r.IsSuccessStatusCode) throw new Exception("load failed: " + r.Content.ReadAsStringAsync().GetAwaiter().GetResult());
    }

    public void Dispose() { try { App.StopAsync().GetAwaiter().GetResult(); } catch { } Http.Dispose(); try { Directory.Delete(Dir, true); } catch { } }

    /// <summary>A self-signed certificate for 127.0.0.1 written to a .pfx file. Returns the path, password and the certificate's SHA-256 fingerprint.</summary>
    public static (string Pfx, string Password, string Fingerprint) SelfSigned()
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=openvp-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder(); san.AddIpAddress(System.Net.IPAddress.Loopback); san.AddDnsName("localhost");
        req.CertificateExtensions.Add(san.Build());
        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        var path = Path.Combine(Path.GetTempPath(), "openvp-test-" + Guid.NewGuid().ToString("N")[..8] + ".pfx");
        File.WriteAllBytes(path, cert.Export(X509ContentType.Pfx, "test-pw"));
        return (path, "test-pw", Convert.ToHexString(SHA256.HashData(cert.RawData)));
    }
}
