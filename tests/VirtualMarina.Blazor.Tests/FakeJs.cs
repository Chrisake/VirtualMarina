using Microsoft.JSInterop;

namespace VirtualMarina.Blazor.Tests;

/// <summary>One call made across the JS interop boundary.</summary>
internal sealed record JsCall(string Identifier, object?[] Args);

/// <summary>
/// Stands in for marinaWebGL.js: records every call and answers from a function, so a test can say what the browser
/// would have returned ("createView" gives view 3, "initRenderer" reports no error) and then read what was sent.
/// </summary>
internal sealed class FakeJsModule(Func<string, object?[], object?>? answer = null) : IJSInProcessObjectReference
{
    private readonly Func<string, object?[], object?> _answer = answer ?? ((_, _) => null);

    public List<JsCall> Calls { get; } = [];

    public bool IsDisposed { get; private set; }

    public JsCall[] CallsTo(string identifier) => Calls.Where(c => c.Identifier == identifier).ToArray();

    public TValue Invoke<TValue>(string identifier, params object?[]? args)
    {
        // Real interop serializes the arguments during the call, so a buffer the caller reuses next frame (as the
        // renderer does with its uniforms) must be recorded as it was then, not as it is when the test looks.
        var arguments = (args ?? []).Select(a => a is Array array ? array.Clone() : a).ToArray();
        Calls.Add(new JsCall(identifier, arguments));
        return _answer(identifier, arguments) is TValue value ? value : default!;
    }

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => ValueTask.FromResult(Invoke<TValue>(identifier, args));

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
        InvokeAsync<TValue>(identifier, args);

    public void Dispose() => IsDisposed = true;

    public ValueTask DisposeAsync()
    {
        IsDisposed = true;
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// An asynchronous-only JS runtime, the kind Blazor Server has. "import" hands out the module; everything else is
/// recorded and answered with the default.
/// </summary>
internal class FakeJsRuntime(FakeJsModule module) : IJSRuntime
{
    public FakeJsModule Module { get; } = module;

    public List<JsCall> Calls { get; } = [];

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
    {
        Calls.Add(new JsCall(identifier, args ?? []));
        return ValueTask.FromResult(identifier == "import" && Module is TValue value ? value : default!);
    }

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
        InvokeAsync<TValue>(identifier, args);
}

/// <summary>The synchronous runtime Blazor WebAssembly has, which is what MarinaView needs to drive WebGL.</summary>
internal sealed class FakeWebAssemblyJsRuntime(FakeJsModule module) : FakeJsRuntime(module), IJSInProcessRuntime
{
    public TResult Invoke<TResult>(string identifier, params object?[]? args)
    {
        Calls.Add(new JsCall(identifier, args ?? []));
        return default!;
    }
}
