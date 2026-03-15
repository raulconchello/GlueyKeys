using System.IO;
using System.Text.Json;
using GlueyKeys.Models;

namespace GlueyKeys.Services;

public class SettingsService
{
    private static readonly string AppDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GlueyKeys");

    private static readonly string SettingsFilePath = Path.Combine(AppDataFolder, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private AppSettings _settings = new();
    private readonly object _lock = new();

    public AppSettings Settings => _settings;

    public event EventHandler? SettingsChanged;

    public void Load()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    var json = File.ReadAllText(SettingsFilePath);
                    _settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
                }
                else
                {
                    _settings = new AppSettings();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load settings: {ex.Message}");
                _settings = new AppSettings();
            }
        }
    }

    public void Save()
    {
        lock (_lock)
        {
            try
            {
                Directory.CreateDirectory(AppDataFolder);
                var json = JsonSerializer.Serialize(_settings, JsonOptions);
                File.WriteAllText(SettingsFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
            }
        }

        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void UpdateSettings(Action<AppSettings> updateAction)
    {
        lock (_lock)
        {
            updateAction(_settings);
        }
        Save();
    }
}
