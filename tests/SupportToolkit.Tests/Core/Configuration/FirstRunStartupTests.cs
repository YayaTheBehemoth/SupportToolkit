
using SupportToolkit.Core.Configuration;

namespace SupportToolkit.Tests.Core.Configuration;

public class FirstRunStartupTests
{
    [Fact]
    public async Task ExistingProfile_SkipsSetupAndStartsInteractive()
    {
        var store = new TestConfigurationStore();

        store.Configuration.Profiles["local-dev"] =
            new SupportToolkitProfile();

        var startup = new FirstRunStartup(store);

        var setupCalls = 0;
        var interactiveCalls = 0;

        var result = await startup.RunAsync(
            () =>
            {
                setupCalls++;
                return Task.FromResult(0);
            },
            () =>
            {
                interactiveCalls++;
                return Task.FromResult(42);
            }
        );

        Assert.Equal(0, setupCalls);
        Assert.Equal(1, interactiveCalls);
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task MissingProfile_RunsSetupThenStartsInteractive()
    {
        var store = new TestConfigurationStore();

        var startup = new FirstRunStartup(store);

        var setupCalls = 0;
        var interactiveCalls = 0;

        var result = await startup.RunAsync(
            () =>
            {
                setupCalls++;

                store.Configuration.Profiles["new-profile"] =
                    new SupportToolkitProfile();

                return Task.FromResult(0);
            },
            () =>
            {
                interactiveCalls++;
                return Task.FromResult(42);
            }
        );

        Assert.Equal(1, setupCalls);
        Assert.Equal(1, interactiveCalls);
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task CancelledSetup_DoesNotStartInteractive()
    {
        var store = new TestConfigurationStore();

        var startup = new FirstRunStartup(store);

        var interactiveCalls = 0;

        var result = await startup.RunAsync(
            () => Task.FromResult(1),
            () =>
            {
                interactiveCalls++;
                return Task.FromResult(0);
            }
        );

        Assert.Equal(1, result);
        Assert.Equal(0, interactiveCalls);
    }

    [Fact]
    public async Task SetupWithoutSavedProfile_DoesNotStartInteractive()
    {
        var store = new TestConfigurationStore();

        var startup = new FirstRunStartup(store);

        var setupCalls = 0;
        var interactiveCalls = 0;

        var result = await startup.RunAsync(
            () =>
            {
                setupCalls++;
                return Task.FromResult(0);
            },
            () =>
            {
                interactiveCalls++;
                return Task.FromResult(0);
            }
        );

        Assert.Equal(1, setupCalls);
        Assert.Equal(0, interactiveCalls);
        Assert.Equal(0, result);
    }

    private sealed class TestConfigurationStore
        : ISupportToolkitConfigurationStore
    {
        public string ConfigurationPath =>
            "test.config.json";

        public SupportToolkitConfiguration Configuration { get; } =
            new();

        public Task<SupportToolkitConfiguration> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                Configuration
            );
        }

        public Task SaveAsync(
            SupportToolkitConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException(
                "Persistence is not required by these tests."
            );
        }
    }
}
