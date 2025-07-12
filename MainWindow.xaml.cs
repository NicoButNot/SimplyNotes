using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Windows.Graphics;
using Windows.Storage.Pickers;
using WinRT;
using WinRT.Interop;

namespace SimpleEditor;

public sealed partial class MainWindow : Window
{
    private WindowsSystemDispatcherQueueHelper _dispatcherQueueHelper;
    private MicaController _micaController;
    private SystemBackdropConfiguration _backdropConfiguration;

    private List<int> _matchIndexes = new();
    private int _currentMatchIndex = -1;

    public MainWindow()
    {
        this.InitializeComponent();

        // Set window size
        IntPtr hwnd = WindowNative.GetWindowHandle(this);
        WindowId windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        AppWindow appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.Resize(new SizeInt32(900, 600));

        SetBackdrop();
        NewTab();
    }

    private void SetBackdrop()
    {
        _dispatcherQueueHelper = new WindowsSystemDispatcherQueueHelper();
        _dispatcherQueueHelper.EnsureWindowsSystemDispatcherQueueController();

        _micaController = new MicaController();
        _backdropConfiguration = new SystemBackdropConfiguration();

        _backdropConfiguration.IsInputActive = true;
        _backdropConfiguration.Theme = SystemBackdropTheme.Default;

        var backdropTarget = this.As<Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop>();
        _micaController.AddSystemBackdropTarget(backdropTarget);
        _micaController.SetSystemBackdropConfiguration(_backdropConfiguration);
    }

    private void NewTab(string header = "Untitled", string content = "")
    {
        var textBox = new TextBox
        {
            Text = content,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Code"),
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var scrollViewer = new ScrollViewer
        {
            Content = textBox,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var tab = new TabViewItem
        {
            Header = header,
            Content = scrollViewer,
            Tag = null
        };

        EditorTabView.TabItems.Add(tab);
        EditorTabView.SelectedItem = tab;
    }

    private async void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        picker.FileTypeFilter.Add("*");

        var file = await picker.PickSingleFileAsync();
        if (file == null) return;

        var text = await File.ReadAllTextAsync(file.Path);
        NewTab(file.Name, text);
        ((TabViewItem)EditorTabView.SelectedItem).Tag = file.Path;
    }

    private async void SaveFile_Click(object sender, RoutedEventArgs e)
    {
        await SaveFileAsync(false);
    }

    private async void SaveFileAs_Click(object sender, RoutedEventArgs e)
    {
        await SaveFileAsync(true);
    }

    private async System.Threading.Tasks.Task SaveFileAsync(bool saveAs)
    {
        if (EditorTabView.SelectedItem is not TabViewItem tab) return;
        if (tab.Content is not ScrollViewer scroll) return;
        if (scroll.Content is not TextBox textBox) return;

        string? path = tab.Tag as string;

        if (path == null || saveAs)
        {
            var picker = new FileSavePicker();
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            picker.FileTypeChoices.Add("Text Document", new[] { ".txt" });
            picker.SuggestedFileName = "Untitled";

            var file = await picker.PickSaveFileAsync();
            if (file == null) return;

            path = file.Path;
            tab.Tag = path;
            tab.Header = file.Name;
        }

        await File.WriteAllTextAsync(path, textBox.Text);
    }

    private void EditorTabView_TabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        EditorTabView.TabItems.Remove(args.Tab);
    }

    private void EditorTabView_AddTabButtonClick(TabView sender, object args)
    {
        NewTab();
    }

    // === Find & Replace Logic ===

    private TextBox? GetCurrentTextBox()
    {
        if (EditorTabView.SelectedItem is not TabViewItem tab) return null;
        if (tab.Content is not ScrollViewer scroll) return null;
        if (scroll.Content is not TextBox textBox) return null;
        return textBox;
    }

    private void ToggleFindReplaceBar_Click(object sender, RoutedEventArgs e)
    {
        if (FindReplaceBar.Visibility == Visibility.Visible)
        {
            FindReplaceBar.Visibility = Visibility.Collapsed;
            _matchIndexes.Clear();
            _currentMatchIndex = -1;
            var tb = GetCurrentTextBox();
            tb?.Focus(FocusState.Programmatic);
        }
        else
        {
            FindReplaceBar.Visibility = Visibility.Visible;
            FindTextBox.Focus(FocusState.Programmatic);
            _matchIndexes.Clear();
            _currentMatchIndex = -1;
        }
    }

    private void FindTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _matchIndexes.Clear();
        _currentMatchIndex = -1;
    }

    private void FindMatches(string search, bool matchCase)
    {
        _matchIndexes.Clear();
        _currentMatchIndex = -1;

        var textBox = GetCurrentTextBox();
        if (textBox == null || string.IsNullOrEmpty(search)) return;

        StringComparison comp = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        int index = 0;

        while (true)
        {
            index = textBox.Text.IndexOf(search, index, comp);
            if (index == -1) break;
            _matchIndexes.Add(index);
            index += search.Length;
        }
    }

    private void HighlightCurrentMatch()
    {
        var textBox = GetCurrentTextBox();
        if (textBox == null) return;
        if (_matchIndexes.Count == 0 || _currentMatchIndex == -1)
        {
            textBox.SelectionLength = 0;
            return;
        }

        int index = _matchIndexes[_currentMatchIndex];
        int length = FindTextBox.Text.Length;

        textBox.Focus(FocusState.Programmatic);
        textBox.SelectionStart = index;
        textBox.SelectionLength = length;
    }

    private void FindNext_Click(object sender, RoutedEventArgs e)
    {
        var search = FindTextBox.Text;
        if (string.IsNullOrEmpty(search)) return;

        var textBox = GetCurrentTextBox();
        if (textBox == null) return;

        bool matchCase = MatchCaseCheckBox.IsChecked == true;

        if (_matchIndexes.Count == 0)
            FindMatches(search, matchCase);

        if (_matchIndexes.Count == 0) return;

        _currentMatchIndex++;
        if (_currentMatchIndex >= _matchIndexes.Count)
            _currentMatchIndex = 0;

        HighlightCurrentMatch();
    }

    private void Replace_Click(object sender, RoutedEventArgs e)
    {
        var textBox = GetCurrentTextBox();
        if (textBox == null) return;

        var search = FindTextBox.Text;
        var replace = ReplaceTextBox.Text;
        if (string.IsNullOrEmpty(search)) return;

        bool matchCase = MatchCaseCheckBox.IsChecked == true;

        if (_matchIndexes.Count == 0)
            FindMatches(search, matchCase);

        if (_matchIndexes.Count == 0 || _currentMatchIndex == -1) return;

        int index = _matchIndexes[_currentMatchIndex];
        textBox.Select(index, search.Length);

        if (string.Compare(textBox.SelectedText, search,
            matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase) == 0)
        {
            textBox.SelectedText = replace;
            textBox.SelectionLength = 0;
            // After replace, update matches
            FindMatches(search, matchCase);
            _currentMatchIndex = Math.Min(_currentMatchIndex, _matchIndexes.Count - 1);
            HighlightCurrentMatch();
        }
    }

    private void ReplaceAll_Click(object sender, RoutedEventArgs e)
    {
        var textBox = GetCurrentTextBox();
        if (textBox == null) return;

        var search = FindTextBox.Text;
        var replace = ReplaceTextBox.Text;
        if (string.IsNullOrEmpty(search)) return;

        bool matchCase = MatchCaseCheckBox.IsChecked == true;

        StringComparison comp = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        // Replace all
        int index = 0;
        var text = textBox.Text;
        var newText = new System.Text.StringBuilder();

        while (index < text.Length)
        {
            int foundIndex = text.IndexOf(search, index, comp);
            if (foundIndex == -1)
            {
                newText.Append(text.Substring(index));
                break;
            }
            newText.Append(text.Substring(index, foundIndex - index));
            newText.Append(replace);
            index = foundIndex + search.Length;
        }

        textBox.Text = newText.ToString();

        // Reset find state
        _matchIndexes.Clear();
        _currentMatchIndex = -1;
    }

    private void CloseFindReplaceBar_Click(object sender, RoutedEventArgs e)
    {
        FindReplaceBar.Visibility = Visibility.Collapsed;
        var textBox = GetCurrentTextBox();
        textBox?.Focus(FocusState.Programmatic);
        _matchIndexes.Clear();
        _currentMatchIndex = -1;
    }
}

// Helper class needed for Mica background dispatcher queue
public class WindowsSystemDispatcherQueueHelper
{
    [DllImport("CoreMessaging.dll")]
    private static extern int CreateDispatcherQueueController(
        DispatcherQueueOptions options,
        out IntPtr dispatcherQueueController);

    [StructLayout(LayoutKind.Sequential)]
    struct DispatcherQueueOptions
    {
        public int dwSize;
        public int threadType;
        public int apartmentType;
    }

    private IntPtr _controller;

    public void EnsureWindowsSystemDispatcherQueueController()
    {
        if (Windows.System.DispatcherQueue.GetForCurrentThread() != null)
            return;

        DispatcherQueueOptions options = new()
        {
            dwSize = Marshal.SizeOf<DispatcherQueueOptions>(),
            threadType = 2,    // DQTYPE_THREAD_CURRENT
            apartmentType = 2  // DQTAT_COM_STA
        };

        CreateDispatcherQueueController(options, out _controller);
    }
}
