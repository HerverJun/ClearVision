using System.Text.Json;
using Acme.Product.Runtime.Abstractions;

namespace Acme.Product.Station;

internal sealed class RuntimeParameterEditor
{
    public RuntimeParameterEditor(
        RuntimeParameterDefinition definition,
        NumericUpDown numericControl,
        double defaultValue)
    {
        Definition = definition;
        NumericControl = numericControl;
        DefaultValue = defaultValue;
    }

    public RuntimeParameterDefinition Definition { get; }

    public NumericUpDown NumericControl { get; }

    public double DefaultValue { get; }

    public double CurrentValue => (double)NumericControl.Value;

    public void SetEnabled(bool enabled)
    {
        NumericControl.Enabled = enabled;
    }
}

internal static class RuntimeParameterControlFactory
{
    public static RuntimeParameterEditor CreateNumericInput(
        RuntimeParameterDefinition definition,
        JsonElement currentValue,
        TableLayoutPanel target,
        int rowIndex)
    {
        var defaultValue = TryGetNumber(definition.DefaultValue, out var parsedDefault)
            ? parsedDefault
            : 0.0d;
        var current = TryGetNumber(currentValue, out var parsedCurrent)
            ? parsedCurrent
            : defaultValue;
        var min = definition.Min ?? 0.0d;
        var max = definition.Max ?? 1.0d;
        var step = definition.Step ?? 0.01d;
        if (max < min)
        {
            (min, max) = (max, min);
        }

        current = Math.Clamp(current, min, max);
        defaultValue = Math.Clamp(defaultValue, min, max);

        var label = new Label
        {
            Text = string.IsNullOrWhiteSpace(definition.DisplayName)
                ? definition.ParameterName
                : definition.DisplayName,
            AutoSize = true,
            Dock = DockStyle.Top,
            Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 4)
        };

        var numeric = new NumericUpDown
        {
            DecimalPlaces = DetermineDecimalPlaces(step),
            Minimum = ToDecimal(min),
            Maximum = ToDecimal(max),
            Increment = ToDecimal(step <= 0 ? 0.01d : step),
            Value = ToDecimal(current),
            Width = 120,
            Margin = new Padding(0, 0, 0, 4)
        };

        var valueRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = new Padding(0)
        };
        valueRow.Controls.Add(numeric);
        valueRow.Controls.Add(new Label
        {
            Text = "下次运行生效",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(8, 4, 0, 0)
        });

        var detail = new Label
        {
            Text = $"默认值：{FormatNumber(defaultValue)}，范围：{FormatNumber(min)} - {FormatNumber(max)}",
            AutoSize = true,
            Dock = DockStyle.Top,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0)
        };

        var container = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 3,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 0, 0, 10)
        };
        container.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        container.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        container.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        container.Controls.Add(label, 0, 0);
        container.Controls.Add(valueRow, 0, 1);
        container.Controls.Add(detail, 0, 2);

        target.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        target.Controls.Add(container, 0, rowIndex);

        return new RuntimeParameterEditor(definition, numeric, defaultValue);
    }

    public static bool TryGetNumber(JsonElement value, out double number)
    {
        number = 0;
        return value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out number);
    }

    public static JsonElement CreateNumberElement(double value)
    {
        return JsonSerializer.SerializeToElement(value);
    }

    public static bool IsSameNumber(double left, double right)
    {
        return Math.Abs(left - right) <= 0.000_000_1d;
    }

    private static int DetermineDecimalPlaces(double step)
    {
        if (step <= 0)
        {
            return 2;
        }

        var text = step.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture);
        var dotIndex = text.IndexOf('.', StringComparison.Ordinal);
        return dotIndex < 0 ? 0 : Math.Min(4, text.Length - dotIndex - 1);
    }

    private static decimal ToDecimal(double value)
    {
        return (decimal)Math.Clamp(value, (double)decimal.MinValue, (double)decimal.MaxValue);
    }

    private static string FormatNumber(double value)
    {
        return value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
    }
}
