using System.Windows.Documents;
using System.Windows.Media;

namespace CemYabansu.PublishInCrm
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
            var paragraph = new Paragraph();
            OutputTextBox.Document = new FlowDocument(paragraph);
            if (isErrorMessage)
            {
                paragraph.Inlines.Add(new Run(text)
                {
                    Foreground = Brushes.Red
                });
            }
            else
            {
                paragraph.Inlines.Add(text);
            }
            paragraph.Inlines.Add(new LineBreak());
        }
    }
}
