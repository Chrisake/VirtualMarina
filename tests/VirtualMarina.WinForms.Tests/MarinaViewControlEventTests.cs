using System.ComponentModel;
using System.Numerics;
using System.Reflection;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Camera;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Input;
using VirtualMarina.WinForms;

namespace VirtualMarina.WinForms.Tests;

/// <summary>
/// MarinaViewControl re-exposes the IMarinaVisualizer events so they can be wired in the Visual Studio designer.
/// These tests keep the two in sync: adding an event to the visualizer without forwarding it fails here.
/// </summary>
public class MarinaViewControlEventTests
{
    private static readonly EventInfo[] VisualizerEvents = typeof(IMarinaVisualizer).GetEvents();

    public static TheoryData<string> VisualizerEventNames()
    {
        var data = new TheoryData<string>();
        foreach (var e in VisualizerEvents) data.Add(e.Name);
        return data;
    }

    [Fact]
    public void Visualizer_HasEvents()
    {
        Assert.NotEmpty(VisualizerEvents); // guards the theories below against silently running zero cases
    }

    [Theory]
    [MemberData(nameof(VisualizerEventNames))]
    public void EveryVisualizerEvent_IsExposedOnTheControl_ForTheDesigner(string eventName)
    {
        var visualizerEvent = typeof(IMarinaVisualizer).GetEvent(eventName)!;
        var controlEvent = typeof(MarinaViewControl).GetEvent(eventName, BindingFlags.Public | BindingFlags.Instance);

        Assert.True(controlEvent is not null, $"MarinaViewControl must forward IMarinaVisualizer.{eventName} so it appears in the designer.");
        Assert.Equal(visualizerEvent.EventHandlerType, controlEvent!.EventHandlerType);

        // Declared on the control itself (not inherited from Control under the same name) and shown in the designer.
        Assert.Equal(typeof(MarinaViewControl), controlEvent.DeclaringType);
        Assert.Equal("Marina", controlEvent.GetCustomAttribute<CategoryAttribute>()?.Category);
        Assert.NotEqual(false, controlEvent.GetCustomAttribute<BrowsableAttribute>()?.Browsable);
        Assert.False(string.IsNullOrWhiteSpace(controlEvent.GetCustomAttribute<DescriptionAttribute>()?.Description),
            $"MarinaViewControl.{eventName} needs a [Description] for the Properties window.");
    }

    [Fact]
    public void EveryVisualizerEvent_IsForwarded_WithTheControlAsSender_AndTheSameEventData()
    {
        using var view = new MarinaViewControl();   // no handle is created, so no OpenGL is needed
        var marina = view.Marina;
        var fromMarina = Recorder.SubscribeAll(marina, typeof(IMarinaVisualizer));
        var fromControl = Recorder.SubscribeAll(view, typeof(MarinaViewControl));

        RaiseEveryEvent(marina);

        foreach (var e in VisualizerEvents)
        {
            var direct = fromMarina.For(e.Name);
            var forwarded = fromControl.For(e.Name);

            Assert.True(direct.Count > 0, $"The test scenario must raise {e.Name}; extend RaiseEveryEvent.");
            Assert.True(direct.Count == forwarded.Count,
                $"MarinaViewControl.{e.Name} was raised {forwarded.Count} time(s), but Marina.{e.Name} {direct.Count} time(s).");
            Assert.All(forwarded, call => Assert.Same(view, call.Sender));
            Assert.Equal(direct.Select(c => c.Args), forwarded.Select(c => c.Args)); // same instances, same order
        }
    }

    [Fact]
    public void SwappingMarina_MovesTheForwardedEvents_ToTheNewVisualizer()
    {
        using var view = new MarinaViewControl();
        var original = view.Marina;
        var replacement = new MarinaVisualizer();
        var fromControl = Recorder.SubscribeAll(view, typeof(MarinaViewControl));

        view.Marina = replacement;
        original.InitializeLayout(SmallLayout());
        Assert.Empty(fromControl.For(nameof(IMarinaVisualizer.LayoutChanged)));

        replacement.InitializeLayout(SmallLayout());
        Assert.Single(fromControl.For(nameof(IMarinaVisualizer.LayoutChanged)));
    }

    [Fact]
    public void DisposingTheControl_StopsForwarding()
    {
        var marina = new MarinaVisualizer();
        var view = new MarinaViewControl(marina);
        var fromControl = Recorder.SubscribeAll(view, typeof(MarinaViewControl));

        view.Dispose();
        marina.InitializeLayout(SmallLayout());

        Assert.Empty(fromControl.For(nameof(IMarinaVisualizer.LayoutChanged)));
    }

    /// <summary>A scenario that raises every IMarinaVisualizer event at least once.</summary>
    private static void RaiseEveryEvent(MarinaVisualizer marina)
    {
        marina.SetViewportSize(800, 600);
        marina.InitializeLayout(SmallLayout());                                    // LayoutChanged
        marina.BerthSelected += (_, e) => e.Actions.Add("checkin", "Check in");

        // Look straight down at a berth so the view center is over it.
        var berth = marina.GetBerth("A-L01")!;
        marina.Camera.SetPose(new CameraPose(new Vector3(berth.Center.X, 0, berth.Center.Y), 0f, 89f, 40f), immediate: true);

        marina.Input.PointerMove(400, 300);                                        // BerthHoverChanged
        marina.Input.PointerDown(400, 300, PointerButton.Left);
        marina.Input.PointerUp(400, 300, PointerButton.Left);                      // SelectionChanged, BerthSelected, PopupChanged, BerthClicked
        marina.AddToSelection("A-L02");                                            // MultiBerthSelected
        marina.SelectBerth("A-L01");
        marina.ShowActions();
        marina.InvokeBerthAction("checkin");                                        // BerthActionInvoked
        marina.AssignBoat("A-L03", new Boat("B1", "Aurora", BoatType.FishingBoat)); // BerthStatusChanged
        marina.ClearSelection();                                                   // SelectionCleared
    }

    private static MarinaLayout SmallLayout() =>
        new MarinaLayoutBuilder("Test")
            .AddPier("A", "Pier A", Vector2.Zero, 0f, 40f, pier => pier.AddBerths(PierSide.Left, 3, 5f, 12f))
            .Build();

    /// <summary>Subscribes to every public event of an object through reflection and records the calls.</summary>
    private sealed class Recorder
    {
        private readonly Dictionary<string, List<(object? Sender, object Args)>> _calls = new();

        public static Recorder SubscribeAll(object target, Type eventSource)
        {
            var recorder = new Recorder();
            foreach (var e in VisualizerEvents)
            {
                var ev = eventSource.GetEvent(e.Name);
                if (ev is null) continue; // the declaration test reports missing events

                var argsType = ev.EventHandlerType!.GetMethod("Invoke")!.GetParameters()[1].ParameterType;
                var sink = new Sink(recorder, e.Name);
                var method = typeof(Sink).GetMethod(nameof(Sink.Handle))!.MakeGenericMethod(argsType);
                ev.AddEventHandler(target, Delegate.CreateDelegate(ev.EventHandlerType, sink, method));
                recorder._calls[e.Name] = new List<(object?, object)>();
            }

            return recorder;
        }

        public IReadOnlyList<(object? Sender, object Args)> For(string eventName) =>
            _calls.TryGetValue(eventName, out var calls) ? calls : Array.Empty<(object?, object)>();

        private sealed class Sink(Recorder recorder, string eventName)
        {
            public void Handle<T>(object? sender, T args) where T : EventArgs => recorder._calls[eventName].Add((sender, args));
        }
    }
}
