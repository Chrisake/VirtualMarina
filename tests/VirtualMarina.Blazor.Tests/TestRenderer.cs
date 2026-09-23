using System.Runtime.ExceptionServices;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;

// BL0006 warns application code off the render tree types, because their shape is an implementation detail of the
// framework. Reading the render tree is the whole job of a test renderer, so here they are the right tool.
#pragma warning disable BL0006

namespace VirtualMarina.Blazor.Tests;

/// <summary>An element of the rendered output, with its attributes, the ids of its event handlers and its text.</summary>
internal sealed class TestElement(string name)
{
    public string Name { get; } = name;

    public Dictionary<string, object?> Attributes { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, ulong> Handlers { get; } = new(StringComparer.Ordinal);

    /// <summary>All the text inside the element, its descendants' included.</summary>
    public string Text { get; set; } = "";

    public string? Attribute(string attributeName) => Attributes.TryGetValue(attributeName, out var value) ? value?.ToString() : null;

    public bool HasClass(string className) => Attribute("class")?.Split(' ').Contains(className) == true;

    public override string ToString() => $"<{Name}> {Text}";
}

/// <summary>A component rendered by a <see cref="TestRenderer"/> of its own, which disposing this disposes.</summary>
internal sealed class Rendered<T> : IDisposable where T : IComponent
{
    public required TestRenderer Renderer { get; init; }

    public required FakeJsRuntime Js { get; init; }

    public T Component { get; set; } = default!;

    public FakeJsModule Module => Js.Module;

    public List<TestElement> Elements() => Renderer.Elements(Component);

    public void Dispose() => Renderer.Dispose();
}

/// <summary>
/// Renders components in memory, with no browser: enough of Blazor to run a component's lifecycle, read what it
/// rendered and raise its DOM events, with <see cref="IJSRuntime"/> the only service it is given.
/// </summary>
internal sealed class TestRenderer(IJSRuntime js) : Renderer(new Services(js), NullLoggerFactory.Instance)
{
    private readonly List<Exception> _errors = [];
    private readonly Dictionary<IComponent, int> _roots = [];

    public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();

    /// <summary>How many times the output was updated, i.e. how many render batches there have been.</summary>
    public int Renders { get; private set; }

    /// <summary>Renders a component on a renderer of its own, with <paramref name="js"/> as its JS runtime.</summary>
    public static async Task<Rendered<T>> RenderAsync<T>(FakeJsRuntime js, IDictionary<string, object?> parameters) where T : IComponent
    {
        var rendered = new Rendered<T> { Renderer = new TestRenderer(js), Js = js };
        try
        {
            rendered.Component = await rendered.Renderer.RenderAsync<T>(parameters);
            return rendered;
        }
        catch
        {
            rendered.Dispose();
            throw;
        }
    }

    public async Task<T> RenderAsync<T>(IDictionary<string, object?> parameters) where T : IComponent
    {
        var component = (T)InstantiateComponent(typeof(T));
        var id = AssignRootComponentId(component);
        _roots[component] = id;
        await Dispatcher.InvokeAsync(() => RenderRootComponentAsync(id, ParameterView.FromDictionary(parameters)));
        ThrowIfFailed();
        return component;
    }

    /// <summary>Runs code on the renderer's dispatcher, the way a real host does when an outside event arrives.</summary>
    public async Task InvokeAsync(Action action)
    {
        await Dispatcher.InvokeAsync(action);
        ThrowIfFailed();
    }

    /// <summary>Every element the component rendered, its child components' included, in document order.</summary>
    public List<TestElement> Elements(IComponent root)
    {
        var elements = new List<TestElement>();
        Collect(_roots[root], elements);
        return elements;
    }

    public Task ClickAsync(TestElement element) => RaiseAsync(element, "onclick", new MouseEventArgs());

    public Task ChangeAsync(TestElement element, object? value) => RaiseAsync(element, "onchange", new ChangeEventArgs { Value = value });

    public async Task RaiseAsync(TestElement element, string eventName, EventArgs args)
    {
        if (!element.Handlers.TryGetValue(eventName, out var handler)) throw new InvalidOperationException($"{element} has no {eventName} handler.");
        await Dispatcher.InvokeAsync(() => DispatchEventAsync(handler, fieldInfo: null, args));
        ThrowIfFailed();
    }

    protected override void HandleException(Exception exception) => _errors.Add(exception);

    protected override Task UpdateDisplayAsync(in RenderBatch renderBatch)
    {
        Renders++;
        return Task.CompletedTask;
    }

    private void ThrowIfFailed()
    {
        if (_errors.Count == 0) return;
        var first = _errors[0];
        _errors.Clear();
        ExceptionDispatchInfo.Capture(first).Throw();
    }

    /// <summary>Reads a component's current output into elements, and returns its text.</summary>
    private string Collect(int componentId, List<TestElement> into)
    {
        var frames = GetCurrentRenderTreeFrames(componentId);
        return Walk(frames.Array, 0, frames.Count, into);
    }

    /// <summary>Reads the frames in [start, end) into elements, and returns the text they contain.</summary>
    private string Walk(RenderTreeFrame[] frames, int start, int end, List<TestElement> into)
    {
        var text = new System.Text.StringBuilder();
        var i = start;
        while (i < end)
        {
            var frame = frames[i];
            switch (frame.FrameType)
            {
                case RenderTreeFrameType.Element:
                    var element = new TestElement(frame.ElementName);
                    into.Add(element);
                    var subtreeEnd = i + frame.ElementSubtreeLength;
                    var child = i + 1;
                    for (; child < subtreeEnd && frames[child].FrameType == RenderTreeFrameType.Attribute; child++)
                    {
                        var attribute = frames[child];
                        if (attribute.AttributeEventHandlerId != 0) element.Handlers[attribute.AttributeName] = attribute.AttributeEventHandlerId;
                        else element.Attributes[attribute.AttributeName] = attribute.AttributeValue;
                    }

                    element.Text = Walk(frames, child, subtreeEnd, into);
                    text.Append(element.Text);
                    i = subtreeEnd;
                    break;

                case RenderTreeFrameType.Region:
                    text.Append(Walk(frames, i + 1, i + frame.RegionSubtreeLength, into));
                    i += frame.RegionSubtreeLength;
                    break;

                case RenderTreeFrameType.Component:
                    text.Append(Collect(frame.ComponentId, into));
                    i += frame.ComponentSubtreeLength;
                    break;

                case RenderTreeFrameType.Text:
                    text.Append(frame.TextContent);
                    i++;
                    break;

                case RenderTreeFrameType.Markup:
                    text.Append(frame.MarkupContent);
                    i++;
                    break;

                default:
                    i++;
                    break;
            }
        }

        return text.ToString();
    }

    /// <summary>The one service the VirtualMarina components and InputFile inject.</summary>
    private sealed class Services(IJSRuntime js) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(IJSRuntime) ? js : null;
    }
}
