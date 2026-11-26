using SupportToolkit.Composition;

namespace SupportToolkit.Cli.Commands;

public sealed partial class ConfigureCommand
{
    /// <summary>
    /// configure lab: adds or resets the "lab" profile, which points at the
    /// second-provider lab started by lab/up.sh, then asks for the lab's
    /// secrets this computer does not have yet. Other profiles and the
    /// active profile are left alone, unless this is the first profile.
    /// </summary>
    private async Task<int> ConfigureLabAsync()
    {
        var configuration = await _configurationStore.LoadAsync();

        var replaced = LabProfileComposition.Apply(configuration);

        var activated = configuration.Profiles.Count == 1;

        if (activated)
        {
            configuration.ActiveProfile = LabProfileComposition.ProfileName;
        }

        await _configurationStore.SaveAsync(configuration);

        Console.WriteLine(
            replaced
                ? "Profile 'lab' reset to the lab's settings."
                : "Profile 'lab' added.");

        Console.WriteLine(
            "  Ticketing:  Zammad     http://127.0.0.1:8100/  (group Users)");
        Console.WriteLine(
            "  Bulk delete: Zammad, with its own deletion token");
        Console.WriteLine(
            "  Backup:     Duplicati  http://127.0.0.1:8201/, http://127.0.0.1:8202/");
        Console.WriteLine(
            "  Commercial: Dolibarr   http://127.0.0.1:8090/");
        Console.WriteLine(
            "  Backup Aggregator mode: production (the lab systems are real)");
        Console.WriteLine();
        Console.WriteLine(
            "The lab must be running (lab/up.sh, or lab\\windows\\start-lab.cmd). " +
            "From another computer, forward ports 8100, 8201, 8202 and 8090 to " +
            "the same numbers.");

        Console.WriteLine();
        Console.WriteLine(
            activated
                ? "It is the only profile, so it is active."
                : "Switch to it with:   SupportToolkit configure use lab" +
                  Environment.NewLine +
                  "and back with:       SupportToolkit configure use <your profile>");

        return await PromptForMissingSecretsAsync(LabProfileComposition.IsLabSecret);
    }
}
