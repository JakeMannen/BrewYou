using FluentAssertions;
using System.Text.Json;

namespace BrewYou.ApiService.Tests;

public class EndpointConfigurationTests
{
    private static string FindRepoRoot()
    {
        var current = AppDomain.CurrentDomain.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "BrewYou.slnx")) || Directory.Exists(Path.Combine(current, ".git")))
            {
                return current;
            }
            var parent = Directory.GetParent(current);
            if (parent == null)
            {
                break;
            }
            current = parent.FullName;
        }
        throw new InvalidOperationException("Could not find repository root from " + AppDomain.CurrentDomain.BaseDirectory);
    }

    [Fact]
    public void LaunchSettings_SpecifiesPort5000ForApiService()
    {
        var repoRoot = FindRepoRoot();
        var launchSettingsPath = Path.Combine(repoRoot, "src", "BrewYou.ApiService", "Properties", "launchSettings.json");
        File.Exists(launchSettingsPath).Should().BeTrue();

        var json = File.ReadAllText(launchSettingsPath);
        using var doc = JsonDocument.Parse(json);
        var profiles = doc.RootElement.GetProperty("profiles");

        var httpProfile = profiles.GetProperty("http");
        var httpUrl = httpProfile.GetProperty("applicationUrl").GetString();
        httpUrl.Should().Be("http://localhost:5000");

        var httpsProfile = profiles.GetProperty("https");
        var httpsUrl = httpsProfile.GetProperty("applicationUrl").GetString();
        httpsUrl.Should().Contain("http://localhost:5000");
    }

    [Fact]
    public void AppHost_SpecifiesPort5000ForBackendAndPort3000ForFrontend()
    {
        var repoRoot = FindRepoRoot();
        var appHostPath = Path.Combine(repoRoot, "src", "BrewYou.AppHost", "AppHost.cs");
        File.Exists(appHostPath).Should().BeTrue();

        var content = File.ReadAllText(appHostPath);
        content.Should().Contain(".WithHttpEndpoint(port: 5000)");
        content.Should().Contain(".WithHttpEndpoint(port: 3000, env: \"PORT\")");
    }

    [Fact]
    public void AppHost_SpecifiesScalarEndpointForApiService()
    {
        var repoRoot = FindRepoRoot();
        var appHostPath = Path.Combine(repoRoot, "src", "BrewYou.AppHost", "AppHost.cs");
        File.Exists(appHostPath).Should().BeTrue();

        var content = File.ReadAllText(appHostPath);
        content.Should().Contain(".WithUrl(\"/scalar/v1\", \"Scalar\")");
    }

    [Fact]
    public void AppHost_SpecifiesMqttBrokerAndPort1883()
    {
        var repoRoot = FindRepoRoot();
        var appHostPath = Path.Combine(repoRoot, "src", "BrewYou.AppHost", "AppHost.cs");
        File.Exists(appHostPath).Should().BeTrue();

        var content = File.ReadAllText(appHostPath);
        content.Should().Contain("builder.AddMosquitto(\"mqtt\", port: 1883)");
        content.Should().Contain(".WithDataVolume(\"brewyou_mqttdata\")");
        content.Should().Contain(".WithReference(mqtt)");
        content.Should().Contain(".WaitFor(mqtt)");
    }

    [Fact]
    public void AppHost_SpecifiesPgAdminForPostgres()
    {
        var repoRoot = FindRepoRoot();
        var appHostPath = Path.Combine(repoRoot, "src", "BrewYou.AppHost", "AppHost.cs");
        File.Exists(appHostPath).Should().BeTrue();

        var content = File.ReadAllText(appHostPath);
        content.Should().Contain(".WithPgAdmin()");
    }

    [Fact]
    public void ViteConfig_SpecifiesPort3000ForFrontendServerAndPreview()
    {
        var repoRoot = FindRepoRoot();
        var viteConfigPath = Path.Combine(repoRoot, "src", "BrewYou.Web", "vite.config.ts");
        File.Exists(viteConfigPath).Should().BeTrue();

        var content = File.ReadAllText(viteConfigPath);
        content.Should().Contain("port: process.env.PORT ? parseInt(process.env.PORT, 10) : 3000");
        content.Should().Contain("port: 3000");
    }

    [Fact]
    public void ApiServiceHttpFile_SpecifiesPort5000()
    {
        var repoRoot = FindRepoRoot();
        var httpFilePath = Path.Combine(repoRoot, "src", "BrewYou.ApiService", "BrewYou.ApiService.http");
        File.Exists(httpFilePath).Should().BeTrue();

        var content = File.ReadAllText(httpFilePath);
        content.Should().Contain("@BrewYou.ApiService_HostAddress = http://localhost:5000");
    }
}