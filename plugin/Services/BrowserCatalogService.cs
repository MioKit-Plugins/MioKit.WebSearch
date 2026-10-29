using Microsoft.Win32;

namespace WebSearch.Services;

internal sealed record BrowserProfileSource(
    string BrowserId,
    string BrowserName,
    string ProfileName,
    string DirectoryPath,
    bool IsFirefox);

public sealed class BrowserCatalogService
{
    public IReadOnlyList<BrowserProfileDto> DiscoverProfiles() =>
        DiscoverProfileSources()
            .Select(x => new BrowserProfileDto
            {
                Browser = x.BrowserName,
                Name = x.ProfileName,
                Path = x.DirectoryPath
            })
            .ToList();

    internal IReadOnlyList<BrowserProfileSource> DiscoverProfileSources()
    {
        var profiles = new List<BrowserProfileSource>();
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        AddChromiumProfiles(profiles, BrowserIds.Edge, "Edge",
            Path.Combine(local, "Microsoft", "Edge", "User Data"));
        AddChromiumProfiles(profiles, BrowserIds.Chrome, "Chrome",
            Path.Combine(local, "Google", "Chrome", "User Data"));

        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var firefoxProfiles = Path.Combine(roaming, "Mozilla", "Firefox", "Profiles");
        if (Directory.Exists(firefoxProfiles))
        {
            foreach (var directory in Directory.EnumerateDirectories(firefoxProfiles))
            {
                if (File.Exists(Path.Combine(directory, "places.sqlite")))
                {
                    profiles.Add(new BrowserProfileSource(
                        BrowserIds.Firefox,
                        "Firefox",
                        Path.GetFileName(directory),
                        directory,
                        true));
                }
            }
        }

        return profiles;
    }

    public IReadOnlyList<BrowserOptionDto> GetBrowserOptions()
    {
        var options = new List<BrowserOptionDto>
        {
            new()
            {
                Id = BrowserIds.SystemDefault,
                Name = "系统默认浏览器",
                IsAvailable = true
            }
        };

        foreach (var (id, name) in new[]
                 {
                     (BrowserIds.Edge, "Microsoft Edge"),
                     (BrowserIds.Chrome, "Google Chrome"),
                     (BrowserIds.Firefox, "Mozilla Firefox")
                 })
        {
            var path = FindBrowserExecutable(id);
            options.Add(new BrowserOptionDto
            {
                Id = id,
                Name = name,
                IsAvailable = path is not null,
                Path = path ?? string.Empty
            });
        }

        return options;
    }

    public string? FindBrowserExecutable(string browserId)
    {
        var executableName = browserId switch
        {
            BrowserIds.Edge => "msedge.exe",
            BrowserIds.Chrome => "chrome.exe",
            BrowserIds.Firefox => "firefox.exe",
            _ => null
        };
        if (executableName is null)
        {
            return null;
        }

        foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var key = root.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{executableName}");
            if (key?.GetValue(null) is string registered && File.Exists(registered))
            {
                return registered;
            }
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string[] candidates = browserId switch
        {
            BrowserIds.Edge =>
            [
                Path.Combine(programFilesX86, "Microsoft", "Edge", "Application", executableName),
                Path.Combine(programFiles, "Microsoft", "Edge", "Application", executableName)
            ],
            BrowserIds.Chrome =>
            [
                Path.Combine(programFiles, "Google", "Chrome", "Application", executableName),
                Path.Combine(programFilesX86, "Google", "Chrome", "Application", executableName),
                Path.Combine(local, "Google", "Chrome", "Application", executableName)
            ],
            _ =>
            [
                Path.Combine(programFiles, "Mozilla Firefox", executableName),
                Path.Combine(programFilesX86, "Mozilla Firefox", executableName)
            ]
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static void AddChromiumProfiles(
        ICollection<BrowserProfileSource> target,
        string browserId,
        string browserName,
        string userDataDirectory)
    {
        if (!Directory.Exists(userDataDirectory))
        {
            return;
        }

        foreach (var directory in Directory.EnumerateDirectories(userDataDirectory))
        {
            if (!File.Exists(Path.Combine(directory, "Bookmarks")))
            {
                continue;
            }

            target.Add(new BrowserProfileSource(
                browserId,
                browserName,
                Path.GetFileName(directory),
                directory,
                false));
        }
    }
}
