using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.Logging;
using SupportToolkit.Providers.BusinessCentral.Auth;
using SupportToolkit.Providers.BusinessCentral.Subscriptions;
using SupportToolkit.Providers.BusinessCentral.Transport;

namespace SupportToolkit.Providers.BusinessCentral;

public sealed record BusinessCentralProviderBundle(
    BusinessCentralOptions Options,
    BusinessCentralSubscriptionProvider Provider
);

/// <summary>
/// The one place the production Business Central provider is built,
/// from a resolved connection.
///
/// Building a provider never signs in. Sign-in happens on the first
/// request, after the request has passed read-only validation.
/// </summary>
public static class BusinessCentralProviderFactory
{
    public static BusinessCentralProviderBundle Create(
        ResolvedBusinessCentralConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var options =
            BusinessCentralOptions.Create(
                connection.TenantId,
                connection.ClientId,
                connection.Environment,
                connection.CompanyId,
                connection.ApiPublisher,
                connection.ApiGroup,
                connection.ApiVersion,
                connection.CompanyName);

        var logger =
            OperationalLogger.FromEnvironment();

        var diagnosticLogger =
            logger.DebugEnabled
                ? logger
                : null;

        var provider =
            BusinessCentralSubscriptionProvider.Create(
                options,
                new MsalBusinessCentralTokenProvider(
                    options,
                    logger: diagnosticLogger),
                diagnosticLogger);

        return new BusinessCentralProviderBundle(
            options,
            provider);
    }
}
