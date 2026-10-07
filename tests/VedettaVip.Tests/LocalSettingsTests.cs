// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

namespace VedettaVip.Tests;

public sealed class LocalSettingsTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("vedettavip-local-").FullName;

    public void Dispose() => Directory.Delete(dir, recursive: true);

    private void File(string name, string key, string value) =>
        System.IO.File.WriteAllText(Path.Combine(dir, name), $$"""{ "{{key}}": "{{value}}" }""");

    private ConfigurationManager Build(Action<ConfigurationManager>? after = null)
    {
        var configuration = new ConfigurationManager();
        configuration.SetFileProvider(new PhysicalFileProvider(dir));
        configuration.AddJsonFile("appsettings.json", optional: true);
        configuration.AddJsonFile("appsettings.Production.json", optional: true);
        after?.Invoke(configuration);
        configuration.AddLocalSettings(new HostingEnvironment { EnvironmentName = Environments.Production });
        return configuration;
    }

    [Fact]
    public void Local_files_override_the_repository_appsettings()
    {
        File("appsettings.json", "A", "repo");
        File("appsettings.Production.json", "B", "repo");
        File("appsettings.Local.json", "A", "local");
        File("appsettings.Production.Local.json", "B", "local-prod");

        var configuration = Build();

        Assert.Equal(("local", "local-prod"), (configuration["A"], configuration["B"]));
    }

    [Fact]
    public void Environment_variables_still_win_over_local_files()
    {
        File("appsettings.Local.json", "A", "local");

        var configuration = Build(c => c.AddInMemoryCollection([new("A", "env")]));

        Assert.Equal("env", configuration["A"]);
    }

    [Fact]
    public void Missing_local_files_are_optional() =>
        Assert.Null(Build()["A"]);
}
