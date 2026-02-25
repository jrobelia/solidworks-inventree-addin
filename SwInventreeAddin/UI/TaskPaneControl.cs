using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using SwInventreeAddin.InvenTree;
using SwInventreeAddin.SolidWorks;

namespace SwInventreeAddin.UI
{
    public class TaskPaneControl : UserControl
    {
        public TextBox PartNumberTextBox { get; private set; }
        public TextBox NamePreviewTextBox { get; private set; }
        public TextBox NotesPreviewTextBox { get; private set; }
        public TextBox RevisionPreviewTextBox { get; private set; }
        public Button ApplyButton { get; private set; }
        public Label StatusLabel { get; private set; }

        private readonly IInventreeClient _client;
        private readonly IDocumentPropertyService _propertyService;

        public TaskPaneControl(IInventreeClient client, IDocumentPropertyService propertyService)
        {
            _client = client;
            _propertyService = propertyService;
            throw new NotImplementedException();
        }

        public Task FetchPartAsync()
        {
            throw new NotImplementedException();
        }

        public void ApplyToDocument()
        {
            throw new NotImplementedException();
        }
    }
}
