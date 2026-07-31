using System.Runtime.Versioning;
using System.Windows;

namespace OpenReader.UI.Dialogs;

[SupportedOSPlatform("windows")]
public partial class ExitDialog : Window
{
    /// <summary>True after the user clicked Yes; false on No / Cancel / X.</summary>
    /// <remarks>
    /// We can't use <see cref="Window.DialogResult"/> because that property
    /// throws <see cref="System.InvalidOperationException"/> when the window
    /// was opened with <c>Show()</c> instead of <c>ShowDialog()</c> — which is
    /// our case (the dialog is non-modal so the host stays responsive).
    /// </remarks>
    public bool ConfirmExit { get; private set; }

    public ExitDialog()
    {
        InitializeComponent();
    }

    private void YesButton_Click(object sender, RoutedEventArgs e)
    {
        ConfirmExit = true;
        Close();
    }

    private void NoButton_Click(object sender, RoutedEventArgs e)
    {
        ConfirmExit = false;
        Close();
    }
}
