namespace SupportToolkit.Core.Modules;

public interface ISupportToolkitModule
{
    string Command { get; }

    string Description { get; }

    Task<int> RunAsync(string[] args);
}