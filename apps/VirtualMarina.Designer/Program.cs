namespace VirtualMarina.Designer;

/// <summary>Entry point of the VirtualMarina Designer.</summary>
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        using var form = new MainForm();
        Application.Run(form);
    }
}
