using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Endo.Core.Ai;
using Endo.Core.Commands;
using Endo.Core.Projects;

namespace Endo.Gui.Views;

/// <summary>
/// Guided project creation: Category/SubCategory/Name, IDE, and Runtime up front, then — for the
/// first project under a new GameModding game — runs the exact same discovery
/// (<see cref="AiOrchestrator.FindCandidatesAsync"/>) the chat's auto-chain uses, and lets the user
/// check which found tools to actually install rather than installing everything automatically.
/// Plain code-behind, matching <c>SetupWindow</c>: a one-shot form, not ongoing state to keep a
/// ViewModel in sync with.
/// </summary>
public partial class NewProjectWindow : Window
{
    private readonly CommandEngine _commandEngine;
    private readonly CommandContext _context;
    private readonly AiOrchestrator _orchestrator;

    private string? _discoveryCategory;
    private string? _discoverySubCategory;

    public NewProjectWindow(CommandEngine commandEngine, CommandContext context, AiOrchestrator orchestrator)
    {
        InitializeComponent();
        _commandEngine = commandEngine;
        _context = context;
        _orchestrator = orchestrator;

        var state = context.Environment ??= context.EnvironmentRepository.Load();

        var knownCategories = state.Projects.Keys
            .Select(k => k.Split('/')[0])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (!knownCategories.Contains("GameModding", StringComparer.OrdinalIgnoreCase))
        {
            knownCategories.Insert(0, "GameModding");
        }
        CategoryCombo.ItemsSource = knownCategories;

        var knownSubCategories = state.Projects.Keys
            .Select(k => k.Split('/'))
            .Where(parts => parts.Length > 1)
            .Select(parts => parts[1])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();
        SubCategoryCombo.ItemsSource = knownSubCategories;

        var ideOptions = new List<string> { "(none)" };
        ideOptions.AddRange(ProjectLauncher.KnownIdeAliases);
        ideOptions.Add("Other...");
        IdeCombo.ItemsSource = ideOptions;
        IdeCombo.SelectedIndex = 0;

        var templateOptions = new List<string> { "(none)" };
        templateOptions.AddRange(ProjectTemplates.Known.Where(t => t != ProjectTemplates.None));
        TemplateCombo.ItemsSource = templateOptions;
        TemplateCombo.SelectedIndex = 0;

        if (state.Runtimes.Count == 0)
        {
            RuntimeLabel.Visibility = Visibility.Collapsed;
            RuntimeCombo.Visibility = Visibility.Collapsed;
        }
        else
        {
            var runtimeOptions = new List<string> { "(none)" };
            runtimeOptions.AddRange(state.Runtimes.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
            RuntimeCombo.ItemsSource = runtimeOptions;
            RuntimeCombo.SelectedIndex = 0;
        }
    }

    private void IdeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        IdeCustomBox.Visibility = IdeCombo.SelectedItem as string == "Other..." ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void CreateButton_Click(object sender, RoutedEventArgs e)
    {
        var category = CategoryCombo.Text.Trim();
        var subCategory = SubCategoryCombo.Text.Trim();
        var name = NameBox.Text.Trim();

        if (category.Length == 0 || subCategory.Length == 0 || name.Length == 0)
        {
            SetStatus("Category, SubCategory, and project name are all required.", isError: true);
            return;
        }

        CreateButton.IsEnabled = false;

        var ide = ResolveIde();
        var args = new Dictionary<string, string> { ["category"] = category, ["subCategory"] = subCategory, ["name"] = name };
        if (!string.IsNullOrWhiteSpace(ide))
        {
            args["ide"] = ide;
        }

        var template = TemplateCombo.SelectedItem as string;
        if (!string.IsNullOrWhiteSpace(template) && template != "(none)")
        {
            args["template"] = template;
        }

        var result = _commandEngine.Execute("project.new", _context, args);
        if (!result.Success)
        {
            SetStatus(result.Error ?? result.Output, isError: true);
            CreateButton.IsEnabled = true;
            return;
        }

        var statusLines = new List<string> { result.Output };

        var selectedRuntime = RuntimeCombo.SelectedItem as string;
        if (!string.IsNullOrWhiteSpace(selectedRuntime) && selectedRuntime != "(none)")
        {
            var projectKey = $"{category}/{subCategory}/{name}";
            var runtimeResult = _commandEngine.Execute("runtime.set", _context,
                new Dictionary<string, string> { ["project"] = projectKey, ["runtime"] = selectedRuntime });
            statusLines.Add(runtimeResult.Success ? runtimeResult.Output : $"(runtime not applied: {runtimeResult.Error})");
        }

        SetStatus(string.Join("\n", statusLines), isError: false);

        var state = _context.Environment ??= _context.EnvironmentRepository.Load();
        if (category.Equals("GameModding", StringComparison.OrdinalIgnoreCase))
        {
            var prefix = $"{category}/{subCategory}/";
            var projectsForThisGame = state.Projects.Keys.Count(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            if (projectsForThisGame == 1)
            {
                await RunDiscoveryAsync(category, subCategory);
            }
        }
    }

    private string? ResolveIde()
    {
        var selected = IdeCombo.SelectedItem as string;
        return selected switch
        {
            null or "(none)" => null,
            "Other..." => string.IsNullOrWhiteSpace(IdeCustomBox.Text) ? null : IdeCustomBox.Text.Trim(),
            _ => selected,
        };
    }

    private async Task RunDiscoveryAsync(string category, string subCategory)
    {
        _discoveryCategory = category;
        _discoverySubCategory = subCategory;

        DiscoveryPanel.Visibility = Visibility.Visible;
        DiscoveryStatusText.Text = "Searching the web for modding tools for this game...";

        var search = await _orchestrator.FindCandidatesAsync(category, subCategory);
        var wellFormed = search.Candidates
            .Where(c => !string.IsNullOrWhiteSpace(c.Name) && !string.IsNullOrWhiteSpace(c.Repository))
            .ToList();

        if (!search.Success || wellFormed.Count == 0)
        {
            DiscoveryStatusText.Text = search.Message;
            return;
        }

        DiscoveryStatusText.Text = $"Found {wellFormed.Count} candidate(s) — checked ones will be installed:";

        var checkBoxes = wellFormed.Select(candidate => new CheckBox
        {
            Content = string.IsNullOrWhiteSpace(candidate.Notes)
                ? $"{candidate.Name} — {candidate.Repository}"
                : $"{candidate.Name} — {candidate.Repository} ({candidate.Notes})",
            IsChecked = true,
            Tag = candidate,
        }).ToList();
        CandidatesList.ItemsSource = checkBoxes;

        InstallSelectedButton.Visibility = Visibility.Visible;
    }

    private void InstallSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        if (_discoveryCategory is null || _discoverySubCategory is null)
        {
            return;
        }

        var selected = CandidatesList.Items
            .OfType<CheckBox>()
            .Where(cb => cb.IsChecked == true)
            .Select(cb => (DiscoveredToolCandidate)cb.Tag)
            .ToList();

        if (selected.Count == 0)
        {
            DiscoveryStatusText.Text = "Nothing selected — check at least one candidate first.";
            return;
        }

        InstallSelectedButton.IsEnabled = false;

        var report = _orchestrator.InstallCandidates(_discoveryCategory, _discoverySubCategory, selected, _context);
        var lines = report.Results.Select(r => $"  {(r.Success ? "[ok]" : "[fail]")} {r.Name}: {r.Message}");
        DiscoveryStatusText.Text = $"{report.Message}\n{string.Join("\n", lines)}";

        InstallSelectedButton.Visibility = Visibility.Collapsed;
    }

    private void SetStatus(string message, bool isError)
    {
        StatusMessage.Foreground = isError ? Brushes.IndianRed : Brushes.MediumSeaGreen;
        StatusMessage.Text = message;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
