using VirtualMarina.TestSupport;

namespace VirtualMarina.WinForms.Tests;

/// <summary>
/// The WinForms host control is what an application embeds, so its published surface is a promise too.
/// The check is the same as for the core library; see <c>Docs/16-compatibility.md</c>.
/// </summary>
public class PublicApiTests
{
    [Fact]
    public void WinFormsApi_MatchesTheApprovedBaseline() =>
        ApiBaseline.AssertUnchanged(
            typeof(MarinaViewControl).Assembly,
            Path.Combine("tests", "VirtualMarina.WinForms.Tests", "ApiBaselines"));
}
