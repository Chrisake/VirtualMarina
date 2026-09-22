namespace VirtualMarina.Designer;

/// <summary>Entry point of the VirtualMarina Designer.</summary>
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
