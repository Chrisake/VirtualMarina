namespace VirtualMarina.TestHost.WinForms;

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
