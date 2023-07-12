﻿using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Raii;
using ImGuiNET;
using MareSynchronos.API.Data.Enum;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Utils;
using Microsoft.Extensions.Logging;
using System.Numerics;

namespace MareSynchronos.UI;

public class DataAnalysisUi : WindowMediatorSubscriberBase
{
    private readonly CharacterAnalyzer _characterAnalyzer;
    private bool _hasUpdate = false;
    private Dictionary<ObjectKind, Dictionary<string, CharacterAnalyzer.FileDataEntry>>? _cachedAnalysis;
    private string _selectedHash = string.Empty;
    private ObjectKind _selectedTab;

    public DataAnalysisUi(ILogger<DataAnalysisUi> logger, MareMediator mediator, CharacterAnalyzer characterAnalyzer) : base(logger, mediator, "Mare Character Data Analysis")
    {
        _characterAnalyzer = characterAnalyzer;

        Mediator.Subscribe<CharacterDataAnalyzedMessage>(this, (_) =>
        {
            _hasUpdate = true;
        });
        Mediator.Subscribe<OpenDataAnalysisUiMessage>(this, (_) => Toggle());
        SizeConstraints = new()
        {
            MinimumSize = new()
            {
                X = 800,
                Y = 600
            },
            MaximumSize = new()
            {
                X = 3840,
                Y = 2160
            }
        };
    }

    public override void OnOpen()
    {
        _hasUpdate = true;
        _selectedHash = string.Empty;
    }

    public override void Draw()
    {
        if (_hasUpdate)
        {
            _cachedAnalysis = _characterAnalyzer.LastAnalysis.DeepClone();
            _hasUpdate = false;
        }

        UiSharedService.TextWrapped("This window shows you all files and their sizes that are currently in use through your character and associated entities in Mare");

        if (_cachedAnalysis!.Count == 0) return;

        if (_cachedAnalysis!.Any(c => c.Value.Any(f => !f.Value.IsComputed)))
        {
            bool isAnalyzing = _characterAnalyzer.IsAnalysisRunning;
            if (isAnalyzing)
            {
                UiSharedService.ColorTextWrapped($"Analyzing {_characterAnalyzer.CurrentFile}/{_characterAnalyzer.TotalFiles}",
                    ImGuiColors.DalamudYellow);
                if (UiSharedService.IconTextButton(FontAwesomeIcon.StopCircle, "Cancel analysis"))
                {
                    _characterAnalyzer.CancelAnalyze();
                }
            }
            else
            {
                UiSharedService.ColorTextWrapped("Some entries in the analysis have file size not determined yet, press the button below to analyze your current data",
                    ImGuiColors.DalamudYellow);
                if (UiSharedService.IconTextButton(FontAwesomeIcon.PlayCircle, "Start analysis"))
                {
                    _ = _characterAnalyzer.ComputeAnalysis(false);
                }
            }
        }

        ImGui.Separator();

        ImGui.TextUnformatted("Total files:");
        ImGui.SameLine();
        ImGui.TextUnformatted(_cachedAnalysis!.Values.Sum(c => c.Values.Count).ToString());
        ImGui.SameLine();
        using (var font = ImRaii.PushFont(UiBuilder.IconFont))
        {
            ImGui.TextUnformatted(FontAwesomeIcon.InfoCircle.ToIconString());
        }
        if (ImGui.IsItemHovered())
        {
            string text = "";
            var groupedfiles = _cachedAnalysis.Values.SelectMany(f => f.Values).GroupBy(f => f.FileType, StringComparer.Ordinal);
            text = string.Join(Environment.NewLine, groupedfiles.OrderBy(f => f.Key, StringComparer.Ordinal)
                .Select(f => f.Key + ": " + f.Count() + " files, size: " + UiSharedService.ByteToString(f.Sum(v => v.OriginalSize)) 
                + ", compressed: " + UiSharedService.ByteToString(f.Sum(v => v.CompressedSize))));
            ImGui.SetTooltip(text);
        }
        ImGui.TextUnformatted("Total size (uncompressed):");
        ImGui.SameLine();
        ImGui.TextUnformatted(UiSharedService.ByteToString(_cachedAnalysis!.Sum(c => c.Value.Sum(c => c.Value.OriginalSize))));
        ImGui.TextUnformatted("Total size (compressed):");
        ImGui.SameLine();
        ImGui.TextUnformatted(UiSharedService.ByteToString(_cachedAnalysis!.Sum(c => c.Value.Sum(c => c.Value.CompressedSize))));

        ImGui.Separator();

        using var tabbar = ImRaii.TabBar("objectSelection");
        foreach (var kvp in _cachedAnalysis)
        {
            using var id = ImRaii.PushId(kvp.Key.ToString());
            string tabText = kvp.Key.ToString();
            if (kvp.Value.Any(f => !f.Value.IsComputed)) tabText += " (!)";
            using var tab = ImRaii.TabItem(tabText + "###" + kvp.Key.ToString());
            if (tab.Success)
            {
                ImGui.TextUnformatted("Files for " + kvp.Key);
                ImGui.SameLine();
                ImGui.TextUnformatted(kvp.Value.Count.ToString());
                ImGui.SameLine();
                using (var font = ImRaii.PushFont(UiBuilder.IconFont))
                {
                    ImGui.TextUnformatted(FontAwesomeIcon.InfoCircle.ToIconString());
                }
                if (ImGui.IsItemHovered())
                {
                    string text = "";
                    var groupedfiles = kvp.Value.Select(v => v.Value).GroupBy(f => f.FileType, StringComparer.Ordinal);
                    text = string.Join(Environment.NewLine, groupedfiles.OrderBy(f => f.Key, StringComparer.Ordinal)
                        .Select(f => f.Key + ": " + f.Count() + " files, size: " + UiSharedService.ByteToString(f.Sum(v => v.OriginalSize))
                        + ", compressed: " + UiSharedService.ByteToString(f.Sum(v => v.CompressedSize))));
                    ImGui.SetTooltip(text);
                }
                ImGui.TextUnformatted($"{kvp.Key} size (uncompressed):");
                ImGui.SameLine();
                ImGui.TextUnformatted(UiSharedService.ByteToString(kvp.Value.Sum(c => c.Value.OriginalSize)));
                ImGui.TextUnformatted($"{kvp.Key} size (compressed):");
                ImGui.SameLine();
                ImGui.TextUnformatted(UiSharedService.ByteToString(kvp.Value.Sum(c => c.Value.CompressedSize)));

                ImGui.Separator();
                if (_selectedTab != kvp.Key)
                {
                    _selectedHash = string.Empty;
                    _selectedTab = kvp.Key;
                }

                using var table = ImRaii.Table("Analysis", 6, ImGuiTableFlags.Sortable | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingFixedFit,
                    new Vector2(0, 300));
                if (!table.Success) continue;
                ImGui.TableSetupColumn("Type");
                ImGui.TableSetupColumn("Hash");
                ImGui.TableSetupColumn("Filepaths");
                ImGui.TableSetupColumn("Gamepaths");
                ImGui.TableSetupColumn("Original Size");
                ImGui.TableSetupColumn("Compressed Size");
                ImGui.TableSetupScrollFreeze(0, 1);
                ImGui.TableHeadersRow();

                var sortSpecs = ImGui.TableGetSortSpecs();
                if (sortSpecs.SpecsDirty)
                {
                    var idx = sortSpecs.Specs.ColumnIndex;

                    if (idx == 1 && sortSpecs.Specs.SortDirection == ImGuiSortDirection.Ascending)
                        _cachedAnalysis[kvp.Key] = kvp.Value.OrderBy(k => k.Value.FileType, StringComparer.Ordinal).ToDictionary(d => d.Key, d => d.Value, StringComparer.Ordinal);
                    if (idx == 1 && sortSpecs.Specs.SortDirection == ImGuiSortDirection.Descending)
                        _cachedAnalysis[kvp.Key] = kvp.Value.OrderByDescending(k => k.Value.FileType, StringComparer.Ordinal).ToDictionary(d => d.Key, d => d.Value, StringComparer.Ordinal);
                    if (idx == 1 && sortSpecs.Specs.SortDirection == ImGuiSortDirection.Ascending)
                        _cachedAnalysis[kvp.Key] = kvp.Value.OrderBy(k => k.Key, StringComparer.Ordinal).ToDictionary(d => d.Key, d => d.Value, StringComparer.Ordinal);
                    if (idx == 1 && sortSpecs.Specs.SortDirection == ImGuiSortDirection.Descending)
                        _cachedAnalysis[kvp.Key] = kvp.Value.OrderByDescending(k => k.Key, StringComparer.Ordinal).ToDictionary(d => d.Key, d => d.Value, StringComparer.Ordinal);
                    if (idx == 2 && sortSpecs.Specs.SortDirection == ImGuiSortDirection.Ascending)
                        _cachedAnalysis[kvp.Key] = kvp.Value.OrderBy(k => k.Value.FilePaths.Count).ToDictionary(d => d.Key, d => d.Value, StringComparer.Ordinal);
                    if (idx == 2 && sortSpecs.Specs.SortDirection == ImGuiSortDirection.Descending)
                        _cachedAnalysis[kvp.Key] = kvp.Value.OrderByDescending(k => k.Value.FilePaths.Count).ToDictionary(d => d.Key, d => d.Value, StringComparer.Ordinal);
                    if (idx == 3 && sortSpecs.Specs.SortDirection == ImGuiSortDirection.Ascending)
                        _cachedAnalysis[kvp.Key] = kvp.Value.OrderBy(k => k.Value.GamePaths.Count).ToDictionary(d => d.Key, d => d.Value, StringComparer.Ordinal);
                    if (idx == 3 && sortSpecs.Specs.SortDirection == ImGuiSortDirection.Descending)
                        _cachedAnalysis[kvp.Key] = kvp.Value.OrderByDescending(k => k.Value.GamePaths.Count).ToDictionary(d => d.Key, d => d.Value, StringComparer.Ordinal);
                    if (idx == 4 && sortSpecs.Specs.SortDirection == ImGuiSortDirection.Ascending)
                        _cachedAnalysis[kvp.Key] = kvp.Value.OrderBy(k => k.Value.OriginalSize).ToDictionary(d => d.Key, d => d.Value, StringComparer.Ordinal);
                    if (idx == 4 && sortSpecs.Specs.SortDirection == ImGuiSortDirection.Descending)
                        _cachedAnalysis[kvp.Key] = kvp.Value.OrderByDescending(k => k.Value.OriginalSize).ToDictionary(d => d.Key, d => d.Value, StringComparer.Ordinal);
                    if (idx == 5 && sortSpecs.Specs.SortDirection == ImGuiSortDirection.Ascending)
                        _cachedAnalysis[kvp.Key] = kvp.Value.OrderBy(k => k.Value.CompressedSize).ToDictionary(d => d.Key, d => d.Value, StringComparer.Ordinal);
                    if (idx == 5 && sortSpecs.Specs.SortDirection == ImGuiSortDirection.Descending)
                        _cachedAnalysis[kvp.Key] = kvp.Value.OrderByDescending(k => k.Value.CompressedSize).ToDictionary(d => d.Key, d => d.Value, StringComparer.Ordinal);

                    sortSpecs.SpecsDirty = false;
                }

                foreach (var item in kvp.Value)
                {
                    using var text = ImRaii.PushColor(ImGuiCol.Text, new Vector4(0, 0, 0, 1), string.Equals(item.Key, _selectedHash));
                    using var text2 = ImRaii.PushColor(ImGuiCol.Text, new Vector4(1, 1, 1, 1), !item.Value.IsComputed);
                    ImGui.TableNextColumn();
                    if (!item.Value.IsComputed)
                    {
                        ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg1, UiSharedService.Color(ImGuiColors.DalamudRed));
                        ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, UiSharedService.Color(ImGuiColors.DalamudRed));
                    }
                    if (string.Equals(_selectedHash, item.Key, StringComparison.Ordinal))
                    {
                        ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg1, UiSharedService.Color(ImGuiColors.DalamudYellow));
                        ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, UiSharedService.Color(ImGuiColors.DalamudYellow));
                    }
                    ImGui.TextUnformatted(item.Value.FileType);
                    if (ImGui.IsItemClicked()) _selectedHash = item.Key;
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(item.Key);
                    if (ImGui.IsItemClicked()) _selectedHash = item.Key;
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(item.Value.FilePaths.Count.ToString());
                    if (ImGui.IsItemClicked()) _selectedHash = item.Key;
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(item.Value.GamePaths.Count.ToString());
                    if (ImGui.IsItemClicked()) _selectedHash = item.Key;
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(UiSharedService.ByteToString(item.Value.OriginalSize));
                    if (ImGui.IsItemClicked()) _selectedHash = item.Key;
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(UiSharedService.ByteToString(item.Value.CompressedSize));
                    if (ImGui.IsItemClicked()) _selectedHash = item.Key;
                }
            }
        }
        ImGui.Separator();
        ImGui.Text("Selected file:");
        ImGui.SameLine();
        UiSharedService.ColorText(_selectedHash, ImGuiColors.DalamudYellow);
        if (_cachedAnalysis[_selectedTab].ContainsKey(_selectedHash))
        {
            var filePaths = _cachedAnalysis[_selectedTab][_selectedHash].FilePaths;
            ImGui.TextUnformatted("Local file path:");
            ImGui.SameLine();
            UiSharedService.TextWrapped(filePaths[0]);
            if (filePaths.Count > 1)
            {
                ImGui.SameLine();
                ImGui.TextUnformatted($"(and {filePaths.Count - 1} more)");
                ImGui.SameLine();
                UiSharedService.FontText(FontAwesomeIcon.InfoCircle.ToIconString(), UiBuilder.IconFont);
                UiSharedService.AttachToolTip(string.Join(Environment.NewLine, filePaths.Skip(1)));
            }

            var gamepaths = _cachedAnalysis[_selectedTab][_selectedHash].GamePaths;
            ImGui.TextUnformatted("Used by game path:");
            ImGui.SameLine();
            UiSharedService.TextWrapped(gamepaths[0]);
            if (gamepaths.Count > 1)
            {
                ImGui.SameLine();
                ImGui.TextUnformatted($"(and {gamepaths.Count - 1} more)");
                ImGui.SameLine();
                UiSharedService.FontText(FontAwesomeIcon.InfoCircle.ToIconString(), UiBuilder.IconFont);
                UiSharedService.AttachToolTip(string.Join(Environment.NewLine, gamepaths.Skip(1)));
            }
        }
    }
}