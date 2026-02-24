using System.Windows.Controls;

namespace SQLRestoreFromBlob.Views;

public partial class RestoreView : UserControl
{
    public RestoreView()
    {
        InitializeComponent();
    }

    private void ExecutionLogBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            textBox.CaretIndex = textBox.Text.Length;
            textBox.ScrollToEnd();
        }
    }
}
