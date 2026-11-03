using Avalonia;
using Avalonia.Controls;

namespace HikariHopper.Views;

/// <summary>
/// Qt's <c>IntValidator { bottom; top }</c> for a <see cref="TextBox"/>: edits that make the text
/// <see cref="State.Invalid"/> are undone, the way a validated <c>TextInput</c> refuses them, and
/// <see cref="IsAcceptable"/> is QML <c>acceptableInput</c>.
/// </summary>
/// <remarks>
/// Port of <c>QIntValidator::validate</c> for the C locale: an optional sign followed by digits.
/// A value below <c>bottom</c>, or above <c>top</c> with no more digits than <c>top</c>, is
/// <see cref="State.Intermediate"/> so it can still be typed.
/// </remarks>
public sealed class QtIntValidator
{
    private readonly TextBox textBox;
    private string accepted;
    private bool reverting;

    public enum State
    {
        Invalid,
        Intermediate,
        Acceptable,
    }

    public QtIntValidator(TextBox textBox, int bottom, int top)
    {
        this.textBox = textBox;
        Bottom = bottom;
        Top = top;
        accepted = textBox.Text ?? "";
        textBox.PropertyChanged += FieldChanged;
    }

    public int Bottom { get; set; }

    public int Top { get; set; }

    public bool IsAcceptable => Validate(textBox.Text ?? "", Bottom, Top) == State.Acceptable;

    public static State Validate(string input, int bottom, int top)
    {
        if (input.Length == 0)
            return State.Intermediate;
        bool minus = input[0] == '-', plus = input[0] == '+';
        string digits = minus || plus ? input[1..] : input;
        if (!digits.All(char.IsAsciiDigit))
            return State.Invalid;
        if (bottom >= 0 && minus)
            return State.Invalid;
        if (top < 0 && plus)
            return State.Invalid;
        if (digits.Length == 0)
            return State.Intermediate;
        if (!long.TryParse(digits, out long entered))
            return State.Invalid;
        if (minus)
            entered = -entered;
        if (entered >= bottom && entered <= top)
            return State.Acceptable;
        if (entered >= 0)
        {
            int length = digits.Length;
            int topLength = top != 0 ? (int)Math.Log10(Math.Abs((double)top)) + 1 : 1;
            return entered > top && -entered < bottom && length > topLength ? State.Invalid : State.Intermediate;
        }
        return entered < bottom ? State.Invalid : State.Intermediate;
    }

    /// <summary>JavaScript <c>Number(text)</c> stored in a QML <c>int</c>: 0 for empty or non-numeric text.</summary>
    public static int NumberOf(string text)
    {
        string trimmed = text.Trim();
        if (trimmed.Length == 0 || !double.TryParse(trimmed, System.Globalization.NumberStyles.AllowLeadingSign,
                System.Globalization.CultureInfo.InvariantCulture, out double value))
            return 0;
        return value > int.MaxValue || value < int.MinValue ? 0 : (int)value;
    }

    private void FieldChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != TextBox.TextProperty || reverting)
            return;
        string text = textBox.Text ?? "";
        if (Validate(text, Bottom, Top) != State.Invalid)
        {
            accepted = text;
            return;
        }
        int caret = Math.Min(Math.Max(0, textBox.CaretIndex - (text.Length - accepted.Length)), accepted.Length);
        reverting = true;
        textBox.Text = accepted;
        textBox.CaretIndex = caret;
        reverting = false;
    }
}
