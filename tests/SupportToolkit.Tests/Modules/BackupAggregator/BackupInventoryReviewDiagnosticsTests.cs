
using System.Net;
using SupportToolkit.Core.Logging;
using SupportToolkit.Modules.BackupAggregator.Services;
using SupportToolkit.Providers.Acronis.Devices;
using SupportToolkit.Providers.Acronis.Devices.Dtos;
using SupportToolkit.Providers.Acronis.Microsoft365;
using SupportToolkit.Providers.Acronis.Microsoft365.Dtos;
using SupportToolkit.Providers.Acronis.Tenants;
using SupportToolkit.Providers.Acronis.Tenants.Dtos;

namespace SupportToolkit.Tests.Modules.BackupAggregator;

public class BackupInventoryReviewDiagnosticsTests
{
    private const string SensitiveMarker =
        "synthetic-secret-value-do-not-log";

    [Fact]
    public async Task Microsoft365HttpFailure_LogsTypeAndStatus_NotRawMessage()
    {
        var failure =
            new HttpRequestException(
                $"Request failed: token={SensitiveMarker}" +
                "&after=synthetic-cursor",
                null,
                HttpStatusCode.Forbidden);

        using var output =
            new StringWriter();

        var service =
            CreateService(
                microsoft365Failure: failure,
                deviceFailure: null,
                output);

        var report =
            await service.ReviewAllTenantsAsync();

        var tenantFailure =
            Assert.Single(report.Failures);

        Assert.Equal(
            "Microsoft 365 inventory",
            tenantFailure.Stage);

        Assert.Contains(
            "403",
            tenantFailure.Reason);

        Assert.Equal(
            0,
            report.TenantCount);

        var diagnostics =
            output.ToString();

        Assert.Contains(
            "Microsoft 365 inventory",
            diagnostics);

        Assert.Contains(
            "HttpRequestException (HTTP 403)",
            diagnostics);

        Assert.DoesNotContain(
            SensitiveMarker,
            diagnostics);

        Assert.DoesNotContain(
            "synthetic-cursor",
            diagnostics);

        Assert.DoesNotContain(
            SensitiveMarker,
            tenantFailure.Reason);
    }

    [Fact]
    public async Task DeviceFailure_LogsExceptionType_NotRawMessage()
    {
        var failure =
            new InvalidOperationException(
                $"Vendor error contained {SensitiveMarker}");

        using var output =
            new StringWriter();

        var service =
            CreateService(
                microsoft365Failure: null,
                deviceFailure: failure,
                output);

        var report =
            await service.ReviewAllTenantsAsync();

        var tenantFailure =
            Assert.Single(report.Failures);

        Assert.Equal(
            "Device inventory",
            tenantFailure.Stage);

        var diagnostics =
            output.ToString();

        Assert.Contains(
            "Device inventory",
            diagnostics);

        Assert.Contains(
            "InvalidOperationException",
            diagnostics);

        Assert.DoesNotContain(
            SensitiveMarker,
            diagnostics);

        Assert.DoesNotContain(
            SensitiveMarker,
            tenantFailure.Reason);
    }

    private static BackupInventoryReviewService CreateService(
        Exception? microsoft365Failure,
        Exception? deviceFailure,
        TextWriter output)
    {
        return new BackupInventoryReviewService(
            new AcronisTenantResolver(
                new SingleTenantProvider()),
            new TestMicrosoft365Provider(
                microsoft365Failure),
            new TestDeviceProvider(
                deviceFailure),
            new BackupInventoryNormalizer(),
            new OperationalLogger(
                debugEnabled: true,
                output: output));
    }

    private sealed class SingleTenantProvider
        : IAcronisTenantProvider
    {
        public Task<IReadOnlyList<TenantDto>> GetTenantsAsync(
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<TenantDto> tenants =
            [
                new TenantDto
                {
                    Id = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                    Name = "Synthetic Customer",
                    Kind = "customer"
                }
            ];

            return Task.FromResult(tenants);
        }
    }

    private sealed class TestMicrosoft365Provider
        : IAcronisMicrosoft365InventoryProvider
    {
        private readonly Exception? _failure;

        public TestMicrosoft365Provider(
            Exception? failure)
        {
            _failure = failure;
        }

        public Task<IReadOnlyList<AcronisMicrosoft365ResourceDto>>
            GetResourcesForTenantAsync(
                string tenantId,
                CancellationToken cancellationToken = default)
        {
            if (_failure is not null)
            {
                return Task.FromException<
                    IReadOnlyList<AcronisMicrosoft365ResourceDto>>(
                        _failure);
            }

            return Task.FromResult<
                IReadOnlyList<AcronisMicrosoft365ResourceDto>>([]);
        }
    }

    private sealed class TestDeviceProvider
        : IAcronisDeviceInventoryProvider
    {
        private readonly Exception? _failure;

        public TestDeviceProvider(
            Exception? failure)
        {
            _failure = failure;
        }

        public Task<IReadOnlyList<AcronisDeviceResourceDto>>
            GetResourcesForTenantAsync(
                string tenantId,
                CancellationToken cancellationToken = default)
        {
            if (_failure is not null)
            {
                return Task.FromException<
                    IReadOnlyList<AcronisDeviceResourceDto>>(
                        _failure);
            }

            return Task.FromResult<
                IReadOnlyList<AcronisDeviceResourceDto>>([]);
        }
    }
}
