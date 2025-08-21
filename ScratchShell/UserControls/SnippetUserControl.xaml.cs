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
    private ObservableCollection<SnippetGroup> groupedSnippets;

    public ObservableCollection<SnippetGroup> GroupedSnippets
    {
        get
        {
            return groupedSnippets;
        }

        set
        {
            groupedSnippets = value;
            OnPropertyChanged(nameof(GroupedSnippets));
        }
    }

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
            SearchSnippet(this);
        }
    }

    public SnippetViewModel SelectedSnippet
    {
        get => _selectedSnippet;
        set
        {
            _selectedSnippet = value;
            if (_selectedSnippet is null || _selectedSnippet.IsSystemSnippet)
            {
                EditButton.IsEnabled = false;
                DeleteButton.IsEnabled = false;
            }
            else if (_selectedSnippet is not null && !_selectedSnippet.IsSystemSnippet)
            {
                EditButton.IsEnabled = true;
                DeleteButton.IsEnabled = true;
            }

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

        // Filter snippets based on search text (search in both name and code)
        List<SnippetViewModel> filteredSnippets = new();
        if (showSystemSnippets.HasValue && showSystemSnippets.Value)
        {
            filteredSnippets = _allSnippets.Where(snippet =>
                (snippet.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                snippet.Code.Contains(searchText, StringComparison.OrdinalIgnoreCase))
            ).ToList();
        }
        else
        {
            filteredSnippets = _allSnippets.Where(snippet =>
                snippet.IsSystemSnippet == showSystemSnippets &&
                 (snippet.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                 snippet.Code.Contains(searchText, StringComparison.OrdinalIgnoreCase))
             ).ToList();
        }
        // Clear selection if current selection is not in filtered results
        if (_selectedSnippet != null && !filteredSnippets.Contains(_selectedSnippet))
        {
            SelectedSnippet = null;
        }
        GroupedSnippets = new ObservableCollection<SnippetGroup>(
            filteredSnippets.GroupBy(s => s.GetCommandCategory())
                    .Select(g => new SnippetGroup
                    {
                        Category = g.Key,
                        Snippets = new ObservableCollection<SnippetViewModel>(g)
                    }));
        OnPropertyChanged(nameof(Snippets));
        OnPropertyChanged(nameof(GroupedSnippets));
        return true;
    }

    /// <summary>
    /// Method to add a new snippet to both collections
    /// </summary>
    public void AddSnippet(SnippetViewModel snippet)
    {
        _allSnippets.Add(snippet);
        _snippets.Add(snippet);
        SearchSnippet(this);
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
        SearchSnippet(this);
    }

    private void SnippetsListBoxSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        SelectedSnippet = e.NewValue as SnippetViewModel;
    }
}

public class SnippetGroup
{
    public string Category { get; set; }
    public ObservableCollection<SnippetViewModel> Snippets { get; set; }
}