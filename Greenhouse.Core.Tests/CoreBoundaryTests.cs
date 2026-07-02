using Greenhouse.Core.Messaging;

namespace Greenhouse.Core.Tests;

/// <summary>
/// Enforces the central scaffolding boundary rule (#16, #20): <c>Greenhouse.Core</c> holds only
/// technology-neutral contracts and must not reference any infrastructure project or transport
/// library. A regression here means an infrastructure type leaked into the application core.
/// </summary>
public class CoreBoundaryTests
{
    [Theory]
    [InlineData("Greenhouse.Mqtt")]
    [InlineData("Greenhouse.Bluetooth")]
    [InlineData("Greenhouse.Network")]
    [InlineData("Greenhouse.Storage")]
    [InlineData("MQTTnet")]
    public void Core_does_not_reference_infrastructure(string forbiddenAssembly)
    {
        var referenced = typeof(IMessagingService).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name);

        Assert.DoesNotContain(forbiddenAssembly, referenced);
    }
}
