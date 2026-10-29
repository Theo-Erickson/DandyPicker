using Avalonia.Markup.Xaml;

namespace DandyPicker
{
    // Loads Styles.axaml (control looks). Added to the app after the Fluent theme, so it wins.
    public partial class DandyStyles : Avalonia.Styling.Styles
    {
        public DandyStyles() { AvaloniaXamlLoader.Load(this); }
    }
}
