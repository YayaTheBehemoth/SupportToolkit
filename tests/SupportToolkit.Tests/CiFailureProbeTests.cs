
namespace SupportToolkit.Tests;

public class CiFailureProbeTests
{
    [Fact]
    public void Ci_ShouldDetectAFailingTest()
    {
        Assert.Fail("Intentional CI failure probe."
);
    }
}
