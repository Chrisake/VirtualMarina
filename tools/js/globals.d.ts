// Globals the shipped scripts publish or consume, declared for the type-check in jsconfig.json.

interface Window {
    /** Set by apps/VirtualMarina.Designer.Blazor/wwwroot/designer.js; called from the designer through IJSRuntime. */
    vmDesigner: Record<string, (...args: any[]) => any>;
}
