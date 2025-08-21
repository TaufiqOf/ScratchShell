using ScratchShell.ViewModels.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Controls;

namespace ScratchShell.UserControls;

/// <summary>
/// Interaction logic for SnippetUserControl.xaml
/// </summary>
public partial class SnippetUserControl : UserControl, INotifyPropertyChanged
{
    private ObservableCollection<SnippetViewModel> _snippets = new();
    private SnippetViewModel _selectedSnippet;

    public delegate Task SnippetHandler(SnippetUserControl obj, SnippetViewModel? snippet);

    public delegate Task SnippetVoidHandler(SnippetUserControl obj);

    public event SnippetVoidHandler OnNewSnippet;

    public event SnippetHandler OnEditSnippet;

    public event SnippetHandler OnDeleteSnippet;

    public event PropertyChangedEventHandler PropertyChanged;

    protected void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public ObservableCollection<SnippetViewModel> Snippets
    {
        get => _snippets;
        set
        {
            _snippets = value;
            OnPropertyChanged(nameof(Snippets));
        }
    }

    public SnippetViewModel SelectedSnippet
    {
        get => _selectedSnippet;
        set
        {
            _selectedSnippet = value;
            OnPropertyChanged(nameof(SelectedSnippet));
        }
    }

    public SnippetUserControl()
    {
        InitializeComponent();
    }

    private void NewButton_Click(object sender, RoutedEventArgs e)
    {
        OnNewSnippet?.Invoke(this);
    }

    private void EditButton_Click(object sender, RoutedEventArgs e)
    {
        OnEditSnippet?.Invoke(this, _selectedSnippet);
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        OnDeleteSnippet?.Invoke(this, _selectedSnippet);
    }

    private void SearchSnippetTextChanged(object sender, TextChangedEventArgs e)
    {

    }
}