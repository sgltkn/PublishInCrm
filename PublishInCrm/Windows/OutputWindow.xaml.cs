namespace CemYabansu.PublishInCrm.Windows
{
    public partial class OutputWindow
    {
        public OutputWindow()
        {
            InitializeComponent();
        }

        public void AddLineToTextBox(string text)
        {
            OutputTextBox.Dispatcher.Invoke(() => AddNewLine(text, false));
        }

        public void AddErrorLineToTextBox(string errorMessage)
        {
            OutputTextBox.Dispatcher.Invoke(() => AddNewLine(errorMessage,true));
        }

        private void AddNewLine(string text, bool isErrorMessage)
        {
            OutputTextBox.AppendText(text + System.Environment.NewLine);
        }
    }
}
