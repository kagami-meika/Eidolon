using System.Windows;
using System.Windows.Markup;

namespace Eidolon.App.Localization;

public class LocExtension : MarkupExtension
{
    public string Key { get; set; } = "";

    public LocExtension() { }

    public LocExtension(string key)
    {
        Key = key;
    }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (string.IsNullOrEmpty(Key)) return Key;
        return SR.Get(Key);
    }
}
