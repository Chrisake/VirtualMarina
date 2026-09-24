using VirtualMarina.Core.Serialization;
using VirtualMarina.Designer.Resources;

namespace VirtualMarina.Designer;

/// <summary>
/// The desktop designer's way of asking: message boxes, the text dialog and the system's file dialogs, all modal to
/// the main window. Every answer is ready by the time the method returns.
/// </summary>
/// <param name="owner">The window the dialogs belong to.</param>
internal sealed class WinFormsDialogs(IWin32Window owner) : IDesignerDialogs
{
    public Task<SaveChangesChoice> AskToSaveChangesAsync(string message)
    {
        var answer = MessageBox.Show(owner, message, Strings.AppName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        return Task.FromResult(answer switch
        {
            DialogResult.Yes => SaveChangesChoice.Save,
            DialogResult.No => SaveChangesChoice.Discard,
            _ => SaveChangesChoice.Cancel,
        });
    }

    public Task<bool> ConfirmAsync(string title, string message) =>
        Task.FromResult(MessageBox.Show(owner, message, title, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes);

    public Task<IReadOnlyList<string>?> PromptAsync(DesignerPrompt prompt)
    {
        using var form = new TextInputForm(prompt);
        return Task.FromResult(form.ShowDialog(owner) == DialogResult.OK ? form.Values : null);
    }

    public Task AlertAsync(string title, string message, DesignerMessageKind kind)
    {
        MessageBox.Show(owner, message, title, MessageBoxButtons.OK, kind == DesignerMessageKind.Warning ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        return Task.CompletedTask;
    }

    public Task<DesignerOpenedFile?> PickDesignAsync()
    {
        using var dialog = new OpenFileDialog { Filter = MarinaDocument.FileDialogFilter, Title = Strings.OpenDesignTitle };
        if (dialog.ShowDialog(owner) != DialogResult.OK) return Task.FromResult<DesignerOpenedFile?>(null);

        var path = dialog.FileName;
        return Task.FromResult<DesignerOpenedFile?>(new DesignerOpenedFile(Path.GetFileName(path), path, () => MarinaDocument.Load(path)));
    }

    /// <summary>
    /// Writes the design, asking where first when it has no file yet. <see cref="MarinaDocument.Save"/> writes next to
    /// the target and only then swaps it in, so a failed write leaves the previous file whole.
    /// </summary>
    public Task<DesignerSavedFile?> SaveDesignAsync(DesignerSaveRequest request)
    {
        var path = request.Location;
        if (path is null)
        {
            using var dialog = new SaveFileDialog
            {
                Filter = MarinaDocument.FileDialogFilter,
                Title = Strings.SaveDesignTitle,
                FileName = request.SuggestedName,
                AddExtension = true,
            };
            if (dialog.ShowDialog(owner) != DialogResult.OK) return Task.FromResult<DesignerSavedFile?>(null);
            path = dialog.FileName;
        }

        request.Document.Save(path);
        return Task.FromResult<DesignerSavedFile?>(new DesignerSavedFile(Path.GetFileName(path), path));
    }
}
