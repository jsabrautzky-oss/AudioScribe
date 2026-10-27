using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AudioScribe.App.Services;

public sealed class AppConfigDto
{
    /// <summary>DPAPI (CurrentUser-Scope)-verschlüsselter Hugging-Face-Token. Leer = nicht gesetzt.</summary>
    [JsonPropertyName("hf_token_dpapi")]
    public string HfTokenProtected { get; set; } = string.Empty;

    [JsonPropertyName("model")]
    public string Model { get; set; } = "large-v3";

    [JsonPropertyName("hf_token_saved")]
    public bool HfTokenSaved { get; set; }
}

/// <summary>
/// Lokale App-Konfiguration in %APPDATA%\AudioScribe\appconfig.json.
/// Der HF-Token wird mit DPAPI (CurrentuserScope, optional Entropy) verschlüsselt.
/// </summary>
public interface IAppConfigStore
{
    AppConfigDto Load();
    void SaveHfToken(string? plainToken);
    string? GetHfToken();
    void SetModel(string model);
    string Model { get; }
}

public sealed class AppDataConfigStore : IAppConfigStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("AudioScribe.v1");
    private readonly object _gate = new();
    private readonly string _dir;
    private readonly string _file;
    private AppConfigDto _cached;

    public AppDataConfigStore()
    {
        _dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AudioScribe");
        _file = Path.Combine(_dir, "appconfig.json");
        Directory.CreateDirectory(_dir);
        _cached = Read();
    }

    public AppConfigDto Load()
    {
        lock (_gate)
        {
            _cached = Read();
            return _cached;
        }
    }

    public string Model
    {
        get { lock (_gate) return _cached.Model; }
    }

    public void SetModel(string model)
    {
        lock (_gate)
        {
            _cached.Model = model;
            Write();
        }
    }

    public void SaveHfToken(string? plainToken)
    {
        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(plainToken))
            {
                _cached.HfTokenProtected = string.Empty;
                _cached.HfTokenSaved = false;
            }
            else
            {
                var protectedBytes = ProtectedData.Protect(
                    Encoding.UTF8.GetBytes(plainToken.Trim()), Entropy, DataProtectionScope.CurrentUser);
                _cached.HfTokenProtected = Convert.ToBase64String(protectedBytes);
                _cached.HfTokenSaved = true;
            }
            Write();
        }
    }

    public string? GetHfToken()
    {
        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(_cached.HfTokenProtected))
                return null;
            try
            {
                var plain = ProtectedData.Unprotect(
                    Convert.FromBase64String(_cached.HfTokenProtected), Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plain);
            }
            catch (CryptographicException)
            {
                // Token war z. B. auf einem anderen Windows-Account verschlüsselt -> verwerfen.
                _cached.HfTokenProtected = string.Empty;
                _cached.HfTokenSaved = false;
                Write();
                return null;
            }
        }
    }

    private AppConfigDto Read()
    {
        if (!File.Exists(_file))
            return new AppConfigDto();
        try
        {
            var json = File.ReadAllText(_file);
            return JsonSerializer.Deserialize<AppConfigDto>(json) ?? new AppConfigDto();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new AppConfigDto();
        }
    }

    private void Write()
    {
        var json = JsonSerializer.Serialize(_cached, new JsonSerializerOptions { WriteIndented = true });
        // atomar: .tmp -> rename, damit ein abstürzender Export die Konfig nicht korrumpiert
        var tmp = _file + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, _file, overwrite: true);
    }
}
