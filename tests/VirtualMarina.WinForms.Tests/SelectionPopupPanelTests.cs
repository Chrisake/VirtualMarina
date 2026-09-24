using System.Numerics;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Domain;

namespace VirtualMarina.WinForms.Tests;

/// <summary>The selection popup as a screen reader sees it.</summary>
public class SelectionPopupPanelTests
{
    [Fact]
    public void TheActionsAndTheCloseButton_SayWhatTheirDefaultActionDoes()
    {
        var marina = new MarinaVisualizer();
        marina.AddPier(new Pier("A", "Pier A", new Vector2(0, -30), 0f, 60f));
        marina.AddBerth(BerthGenerator.AtPier(marina.GetPier("A")!, "A-L01", PierSide.Left, 0f, 5f, 12f));
        marina.BerthSelected += (_, e) => e.Actions.Add("checkin", "Check in");
        marina.SelectBerth("A-L01");
        marina.ShowActions();
        using var popup = new SelectionPopupPanel();
        popup.Present(marina.ActivePopup!);

        var items = Enumerable.Range(0, popup.AccessibilityObject.GetChildCount())
            .Select(popup.AccessibilityObject.GetChild)
            .Where(child => child!.Role is AccessibleRole.MenuItem or AccessibleRole.PushButton)
            .ToList();

        Assert.Equal(2, items.Count); // the action, then the close button
        Assert.All(items, item => Assert.Equal("Press", item!.DefaultAction));
    }
}
