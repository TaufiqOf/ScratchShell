using ScratchShell.ViewModels.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Controls;

namespace ScratchShell.UserControls;

/// <summary>
/// Interaction logic for SnippetUserControl.xaml
/// </summary>
public partial class SnippetUserControl : UserControl, INotifyPropertyChanged
{
    private ObservableCollection<SnippetViewModel> _snippets = new();
    private ObservableCollection<SnippetViewModel> _allSnippets = new(); // Keep track of all snippets for filtering
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
            
            // Update the complete list when the snippets are set
            _allSnippets.Clear();
            foreach (var snippet in value)
            {
                _allSnippets.Add(snippet);
            }
            
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
    private void ShowHideSystemSystemSnippetClick(object sender, RoutedEventArgs e)
    {
        SearchSnippet(sender);
    }
    private void SearchSnippetTextChanged(object sender, TextChangedEventArgs e)
    {
       SearchSnippet(sender);
    }

    private bool SearchSnippet(object sender)
    {
        var searchText = SearchSnippetTextBox.Text?.Trim() ?? string.Empty;
        var showSystemSnippets = SearchSnippetButton.IsChecked;
        // If search is empty, show all snippets
        if (string.IsNullOrEmpty(searchText))
        {
            RestoreAllSnippets();
            return false;
        }

        // Filter snippets based on search text (search in both name and code)
        var filteredSnippets = _allSnippets.Where(snippet => 
            snippet.IsSystemSnippet == showSystemSnippets &&
            (snippet.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
            snippet.Code.Contains(searchText, StringComparison.OrdinalIgnoreCase))
        ).ToList();

        // Update the displayed snippets
        _snippets.Clear();
        foreach (var snippet in filteredSnippets)
        {
            _snippets.Add(snippet);
        }

        // Clear selection if current selection is not in filtered results
        if (_selectedSnippet != null && !filteredSnippets.Contains(_selectedSnippet))
        {
            SelectedSnippet = null;
        }

        OnPropertyChanged(nameof(Snippets));
        return true;
    }

    private void RestoreAllSnippets()
    {
        _snippets.Clear();
        foreach (var snippet in _allSnippets)
        {
            _snippets.Add(snippet);
        }
        OnPropertyChanged(nameof(Snippets));
    }

    /// <summary>
    /// Method to add a new snippet to both collections
    /// </summary>
    public void AddSnippet(SnippetViewModel snippet)
    {
        _allSnippets.Add(snippet);
        _snippets.Add(snippet);
        OnPropertyChanged(nameof(Snippets));
    }

    /// <summary>
    /// Method to remove a snippet from both collections
    /// </summary>
    public void RemoveSnippet(SnippetViewModel snippet)
    {
        _allSnippets.Remove(snippet);
        _snippets.Remove(snippet);
        
        if (_selectedSnippet == snippet)
        {
            SelectedSnippet = null;
        }
        
        OnPropertyChanged(nameof(Snippets));
    }


}