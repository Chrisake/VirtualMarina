using VirtualMarina.TestSupport;

namespace VirtualMarina.Blazor.Tests;

/// <summary>
/// The Blazor component is what a web application embeds, so its published surface is a promise too.
/// The check is the same as for the core library; see <c>Docs/16-compatibility.md</c>.
/// </summary>
public class PublicApiTests
{
    [Fact]
    public void BlazorApi_MatchesTheApprovedBaseline() =>
        ApiBaseline.AssertUnchanged(
            typeof(MarinaView).Assembly,
            Path.Combine("tests", "VirtualMarina.Blazor.Tests", "ApiBaselines"));
}
