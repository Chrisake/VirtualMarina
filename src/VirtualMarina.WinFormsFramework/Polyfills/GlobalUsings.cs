// .NET Framework's System.dll has a System.Diagnostics.InstanceData too, so a file that imports both namespaces (the
// OpenGL renderer) would find the name ambiguous. An alias is looked up before the imported namespaces.
global using InstanceData = VirtualMarina.Core.Rendering.InstanceData;
