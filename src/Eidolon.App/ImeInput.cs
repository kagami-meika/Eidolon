using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Eidolon.App;

public static class ImeInput
{
    public static bool ShouldIgnoreHotkey(KeyEventArgs e)
    {
        // Modifier shortcuts always OK
        if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) != 0)
            return false;

        // Text input owns all bare keys (layer rename, new doc size, etc.)
        if (Keyboard.FocusedElement is TextBox)
            return true;
        if (Keyboard.FocusedElement is PasswordBox)
            return true;
        if (Keyboard.FocusedElement is ComboBox cb && cb.IsEditable)
            return true;

        try
        {
            // When IME is ON and focus is on a control that accepts text composition
            var ims = InputMethod.Current;
            if (ims != null && ims.ImeState == InputMethodState.On)
            {
                if (Keyboard.FocusedElement is DependencyObject d
                    && InputMethod.GetIsInputMethodEnabled(d)
                    && Keyboard.FocusedElement is not System.Windows.FrameworkElement { Name: "Canvas" }
                    && e.Key is not (Key.Escape or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt))
                {
                    // Space on canvas still pans; only block if TextBox already handled above
                    if (e.Key == Key.Space) return false;
                    // For safety during IME on non-canvas: ignore letter hotkeys
                    if (Keyboard.FocusedElement is Control { Focusable: true }
                        and not System.Windows.Controls.Primitives.ButtonBase
                        and not System.Windows.Controls.Primitives.Selector)
                    {
                        // Menu focused etc. - allow
                    }
                }
            }
        }
        catch { /* ignore */ }

        return false;
    }

    public static void ConfigureForTextInput(UIElement element)
    {
        InputMethod.SetIsInputMethodEnabled(element, true);
        InputMethod.SetPreferredImeState(element, InputMethodState.DoNotCare);
        InputMethod.SetIsInputMethodSuspended(element, false);
    }

    public static void ConfigureForCanvas(UIElement element)
    {
        // Canvas: IME enabled so system doesn't break; hotkeys filtered via ShouldIgnoreHotkey
        InputMethod.SetIsInputMethodEnabled(element, true);
        InputMethod.SetPreferredImeState(element, InputMethodState.Off);
    }
}
