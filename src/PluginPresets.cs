using System;
using System.Collections.Generic;
using BigCityLegacy.Api;
using BigCityLegacy.UI;
using UnityEngine;

namespace DynamicSky
{
    public sealed partial class Plugin
    {
        private enum PresetNotice { None, Saved, Applied, InvalidName, Exists, InvalidValues, StorageError, Deleted, ChangedFile }

        private SkyPresets presetStore;
        private IReadOnlyList<SkyPresets.Preset> savedPresets = Array.Empty<SkyPresets.Preset>();
        private IReadOnlyList<SkyPresets.Preset> standardPresets;
        private readonly HashSet<string> reportedPresetErrors = new HashSet<string>(StringComparer.Ordinal);
        private int presetTab;
        private float nextPresetRefresh;
        private string presetName = "";
        private bool skippedPresets, deletePending;
        private PresetNotice presetNotice;

        private SkyPresets PresetStore => presetStore ?? (presetStore = SkyPresets.ForGame());

        private float DrawPresetPanel(Rect area)
        {
            int selected = DrawTabs(area, 66, new[] { SkyText.Standard, SkyText.Saved }, presetTab);
            if (selected != presetTab)
            {
                presetTab = selected;
                presetNotice = PresetNotice.None;
                nextPresetRefresh = 0;
                scroll.ResetScroll();
            }

            var panel = new LegacyUILayout(new Rect(area.x, area.y + 105, area.width, area.height - 105), 7);
            float offset = 105;
            if (presetTab == 1)
            {
                LegacyUI.Label(panel.Row(22), SkyText.Name);
                var row = panel.Row(28);
                const float folderWidth = 113;
                const float saveWidth = 110;
                presetName = LegacyUI.TextField(new Rect(row.x, row.y, row.width - folderWidth - saveWidth - 12, row.height), presetName);
                if (LegacyUI.GreenButton(new Rect(row.xMax - folderWidth - saveWidth - 6, row.y, saveWidth, row.height), SkyText.Save))
                    SavePreset();
                if (LegacyUI.Button(new Rect(row.xMax - folderWidth, row.y, folderWidth, row.height), SkyText.OpenFolder))
                    OpenPresetFolder();
                offset += 64;
            }

            if (presetNotice != PresetNotice.None)
            {
                LegacyUI.MiniHint(panel.Row(25), PresetNoticeText());
                offset += 32;
            }
            return offset;
        }

        private void DrawPresets(Rect content)
        {
            var layout = new LegacyUILayout(content, 7);
            if (presetTab == 0)
            {
                if (standardPresets == null) standardPresets = SkyPresets.LoadStandard(ReportPresetError);
                foreach (var preset in standardPresets) DrawPresetRow(layout.Row(28), preset, false);
                return;
            }

            if (savedPresets.Count == 0) LegacyUI.Label(layout.Row(22), SkyText.Empty);
            foreach (var preset in savedPresets) DrawPresetRow(layout.Row(28), preset, true);
            if (skippedPresets) LegacyUI.MiniHint(layout.Row(28), SkyText.Skipped);
        }

        private void DrawPresetRow(Rect row, SkyPresets.Preset preset, bool canDelete)
        {
            const float buttonWidth = 100;
            float applyX = row.xMax - buttonWidth - (canDelete ? buttonWidth + 6 : 0);
            LegacyUI.Label(new Rect(row.x, row.y, applyX - row.x - 6, row.height), preset.DisplayName);
            if (LegacyUI.Button(new Rect(applyX, row.y, buttonWidth, row.height), SkyText.Apply))
                ApplyPreset(preset.Values);
            if (canDelete && LegacyUI.DangerButton(new Rect(row.xMax - buttonWidth, row.y, buttonWidth, row.height), SkyText.Delete))
                ConfirmPresetDeletion(preset);
        }

        private void TickSavedPresets()
        {
            if (windowOpen && tab == Tab.Presets && presetTab == 1 && Time.realtimeSinceStartup >= nextPresetRefresh)
                RefreshPresets();
        }

        private void ReportPresetError(string source, Exception error)
        {
            if (reportedPresetErrors.Add(source)) Logger.LogWarning("Cannot read sky preset " + source + ": " + error.Message);
        }

        private void RefreshPresets()
        {
            nextPresetRefresh = Time.realtimeSinceStartup + 1;
            skippedPresets = false;
            try
            {
                savedPresets = PresetStore.LoadSaved((path, error) =>
                {
                    skippedPresets = true;
                    ReportPresetError(path, error);
                });
            }
            catch (Exception error) when (SkyPresets.IsStorageError(error))
            {
                presetNotice = PresetNotice.StorageError;
                ReportPresetError("saved preset directory", error);
            }
        }

        private void OpenPresetFolder()
        {
            try
            {
                PresetStore.OpenFolder();
            }
            catch (Exception error) when (SkyPresets.IsStorageError(error))
            {
                presetNotice = PresetNotice.StorageError;
                ReportPresetError("saved preset directory", error);
            }
        }

        private void ConfirmPresetDeletion(SkyPresets.Preset preset)
        {
            if (deletePending) return;
            deletePending = true;
            try
            {
                LegacyDialogs.Show(new LegacyDialogOptions
                {
                    Title = SkyText.DeleteTitleEn, TitleRu = SkyText.DeleteTitleRu,
                    Message = string.Format(SkyText.DeleteMessageEn, preset.Name),
                    MessageRu = string.Format(SkyText.DeleteMessageRu, preset.Name),
                    NoBtnText = SkyText.CancelEn, NoBtnTextRu = SkyText.CancelRu,
                    YesBtnText = SkyText.DeleteEn, YesBtnTextRu = SkyText.DeleteRu,
                    YesBtnType = LegacyDialogButtonType.Danger
                }, confirmed =>
                {
                    deletePending = false;
                    if (!this || !supported || !confirmed) return;
                    try
                    {
                        presetNotice = PresetStore.Delete(preset) ? PresetNotice.Deleted : PresetNotice.ChangedFile;
                        RefreshPresets();
                    }
                    catch (Exception error) when (SkyPresets.IsStorageError(error))
                    {
                        presetNotice = PresetNotice.StorageError;
                        ReportPresetError(preset.Path, error);
                    }
                });
            }
            catch (ArgumentException error)
            {
                deletePending = false;
                presetNotice = PresetNotice.StorageError;
                ReportPresetError(preset.Path, error);
            }
            catch (InvalidOperationException error)
            {
                deletePending = false;
                presetNotice = PresetNotice.StorageError;
                ReportPresetError(preset.Path, error);
            }
        }

        private void SavePreset()
        {
            try
            {
                var result = PresetStore.Save(presetName, Runtime.Options.Capture());
                switch (result)
                {
                    case SkyPresets.SaveResult.InvalidName:
                        presetNotice = PresetNotice.InvalidName;
                        return;
                    case SkyPresets.SaveResult.Exists:
                        presetNotice = PresetNotice.Exists;
                        return;
                }
                presetNotice = PresetNotice.Saved;
                presetName = "";
                GUI.FocusControl(null);
                RefreshPresets();
            }
            catch (Exception error) when (SkyPresets.IsStorageError(error))
            {
                presetNotice = PresetNotice.StorageError;
                Logger.LogWarning("Cannot save sky preset: " + error.Message);
            }
        }

        private void ApplyPreset(IReadOnlyDictionary<string, string> values)
        {
            if (!Runtime.Options.ApplyPreset(values))
            {
                presetNotice = PresetNotice.InvalidValues;
                return;
            }

            Runtime.Options.Save();
            if (Runtime.IsActive) Runtime.Apply(true);
            else if (Runtime.Options.Enabled) Activate();
            presetNotice = PresetNotice.Applied;
        }

        private string PresetNoticeText()
        {
            switch (presetNotice)
            {
                case PresetNotice.Saved: return SkyText.SavedNotice;
                case PresetNotice.Applied: return SkyText.AppliedNotice;
                case PresetNotice.InvalidName: return SkyText.InvalidName;
                case PresetNotice.Exists: return SkyText.Exists;
                case PresetNotice.InvalidValues: return SkyText.InvalidValues;
                case PresetNotice.StorageError: return SkyText.StorageError;
                case PresetNotice.Deleted: return SkyText.Deleted;
                case PresetNotice.ChangedFile: return SkyText.ChangedFile;
                default: return "";
            }
        }
    }
}
