using Acme.Product.Runtime;
using Acme.Product.Runtime.Abstractions;

namespace Acme.Product.Station;

public sealed class RuntimeParameterPanel : UserControl
{
    private readonly TableLayoutPanel _layout = new();
    private readonly Label _emptyLabel = new();
    private readonly TableLayoutPanel _parameterRows = new();
    private readonly Button _applyButton = new();
    private readonly Button _cancelButton = new();
    private readonly Button _resetButton = new();
    private readonly Button _importButton = new();
    private readonly Button _exportButton = new();
    private readonly List<RuntimeParameterEditor> _editors = [];
    private RuntimePackage? _package;
    private RuntimeSiteProfile? _profile;
    private bool _editingEnabled;

    public RuntimeParameterPanel()
    {
        BuildUi();
    }

    public Action<RuntimeSiteProfile>? ApplyRequested { get; set; }

    public Action? ResetRequested { get; set; }

    public Action? ImportRequested { get; set; }

    public Action<RuntimeSiteProfile>? ExportRequested { get; set; }

    public void LoadPackage(RuntimePackage? package, RuntimeSiteProfile? profile)
    {
        _package = package;
        _profile = profile == null ? null : RuntimeParameterOverrideApplier.CloneProfile(profile);
        Render();
    }

    public void SetEditingEnabled(bool enabled)
    {
        _editingEnabled = enabled;
        foreach (var editor in _editors)
        {
            editor.SetEnabled(enabled);
        }

        var hasEditors = _editors.Count > 0;
        _applyButton.Enabled = enabled && hasEditors;
        _cancelButton.Enabled = enabled && hasEditors;
        _resetButton.Enabled = enabled && hasEditors;
        _importButton.Enabled = enabled && hasEditors;
        _exportButton.Enabled = enabled && hasEditors;
    }

    private void BuildUi()
    {
        Dock = DockStyle.Fill;

        _layout.Dock = DockStyle.Fill;
        _layout.ColumnCount = 1;
        _layout.RowCount = 3;
        _layout.Margin = new Padding(0);
        _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _emptyLabel.AutoSize = true;
        _emptyLabel.Dock = DockStyle.Top;
        _emptyLabel.ForeColor = SystemColors.GrayText;
        _emptyLabel.MaximumSize = new Size(260, 0);
        _emptyLabel.Text = "当前运行包未开放现场参数";

        _parameterRows.Dock = DockStyle.Fill;
        _parameterRows.ColumnCount = 1;
        _parameterRows.Margin = new Padding(0);
        _parameterRows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _parameterRows.AutoScroll = true;

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Margin = new Padding(0, 8, 0, 0)
        };

        ConfigureButton(_applyButton, "应用", OnApply);
        ConfigureButton(_cancelButton, "取消修改", Render);
        ConfigureButton(_resetButton, "恢复默认", () => ResetRequested?.Invoke());
        ConfigureButton(_importButton, "导入", () => ImportRequested?.Invoke());
        ConfigureButton(_exportButton, "导出", OnExport);
        buttons.Controls.AddRange([_applyButton, _cancelButton, _resetButton, _importButton, _exportButton]);

        _layout.Controls.Add(_emptyLabel, 0, 0);
        _layout.Controls.Add(_parameterRows, 0, 1);
        _layout.Controls.Add(buttons, 0, 2);
        Controls.Add(_layout);
        SetEditingEnabled(false);
    }

    private void Render()
    {
        _parameterRows.Controls.Clear();
        _parameterRows.RowStyles.Clear();
        _editors.Clear();

        var definitions = _package?.ParameterSchema.Parameters
            .Where(definition =>
                definition.SiteTunable &&
                definition.ValueType == RuntimeParameterValueType.Number &&
                definition.UiKind == RuntimeParameterUiKind.NumericInput)
            .OrderBy(definition => definition.GroupName, StringComparer.CurrentCulture)
            .ThenBy(definition => definition.Order)
            .ThenBy(definition => definition.DisplayName, StringComparer.CurrentCulture)
            .ToList()
            ?? [];

        if (definitions.Count == 0)
        {
            _emptyLabel.Visible = true;
            _parameterRows.Visible = false;
            SetEditingEnabled(false);
            return;
        }

        _emptyLabel.Visible = false;
        _parameterRows.Visible = true;
        var overridesById = (_profile?.Overrides ?? [])
            .GroupBy(item => item.ParameterId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.Ordinal);

        for (var index = 0; index < definitions.Count; index += 1)
        {
            var definition = definitions[index];
            var currentValue = overridesById.TryGetValue(definition.Id, out var overrideValue)
                ? overrideValue
                : definition.DefaultValue;
            _editors.Add(RuntimeParameterControlFactory.CreateNumericInput(
                definition,
                currentValue,
                _parameterRows,
                index));
        }

        SetEditingEnabled(_editingEnabled);
    }

    private void OnApply()
    {
        var profile = CreateDraftProfile();
        if (profile == null)
        {
            return;
        }

        ApplyRequested?.Invoke(profile);
    }

    private void OnExport()
    {
        var profile = CreateDraftProfile();
        if (profile == null)
        {
            return;
        }

        ExportRequested?.Invoke(profile);
    }

    private RuntimeSiteProfile? CreateDraftProfile()
    {
        if (_package == null)
        {
            return null;
        }

        var profile = _profile == null
            ? new RuntimeSiteProfile()
            : RuntimeParameterOverrideApplier.CloneProfile(_profile);
        profile.ProfileId = string.IsNullOrWhiteSpace(profile.ProfileId) ? "local-site" : profile.ProfileId;
        profile.PackageId = _package.Manifest.PackageId;
        profile.FlowHash = _package.Manifest.FlowHash;
        profile.UpdatedAtUtc = DateTimeOffset.UtcNow;
        profile.UpdatedBy = string.IsNullOrWhiteSpace(profile.UpdatedBy) ? "local-engineer" : profile.UpdatedBy;
        profile.Overrides = _editors
            .Where(editor => !RuntimeParameterControlFactory.IsSameNumber(editor.CurrentValue, editor.DefaultValue))
            .Select(editor => new RuntimeParameterOverride
            {
                ParameterId = editor.Definition.Id,
                Value = RuntimeParameterControlFactory.CreateNumberElement(editor.CurrentValue)
            })
            .ToList();
        return profile;
    }

    private static void ConfigureButton(Button button, string text, Action onClick)
    {
        button.Text = text;
        button.AutoSize = true;
        button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        button.Padding = new Padding(8, 3, 8, 3);
        button.Margin = new Padding(0, 0, 6, 0);
        button.FlatStyle = FlatStyle.System;
        button.Click += (_, _) => onClick();
    }
}
