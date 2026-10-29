using System.Collections.Generic;

namespace DandyPicker
{
    // The Seed Field palette: a night sky, white seed heads and dandelion gold. Every
    // color in the UI is a named brush in the app's resources.
    static class AppTheme
    {
        static readonly Dictionary<string, string> Palette = new Dictionary<string, string>
        {
            {"Bg","#0b0f1d"}, {"Panel","#121831"}, {"PanelHover","#18203d"}, {"Input","#0b0f1d"}, {"RunBg","#16223a"},
            {"Border","#1f2745"}, {"Line","#2c3554"}, {"Dashed","#2f395c"}, {"Text","#e9ecf6"}, {"Sub","#c3c9d8"}, {"Muted","#8e97b3"}, {"Faint","#6d7697"},
            {"Link","#8fb4ff"}, {"Accent","#f2c14e"}, {"AccentText","#1b1405"}, {"SelRow","#18203d"},
            {"WarnBg","#2a2412"}, {"WarnBorder","#5a4a1a"}, {"WarnText","#f3e3b4"},
            {"ErrBg","#2c1618"}, {"ErrBorder","#62302f"}, {"ErrText","#f6c9c6"},
            {"InfoBg","#131a33"}, {"InfoBorder","#27325a"}, {"InfoText","#e9ecf6"},
            {"OkText","#7fe0ad"}, {"OkBorder","#3c7d64"},
            {"DotOk","#4cc38a"}, {"DotWarn","#e8b339"}, {"DotErr","#f2706b"}, {"DotInfo","#8fb4ff"},
            {"Scroll","#2c3554"},
        };

        // Fluent's own brushes for the controls DandyPicker doesn't restyle, pointed at ours.
        static readonly Dictionary<string, string> FluentKeys = new Dictionary<string, string>
        {
            {"TextControlForeground","Text"}, {"TextControlForegroundPointerOver","Text"}, {"TextControlForegroundFocused","Text"},
            {"TextControlBackground","Input"}, {"TextControlBackgroundPointerOver","Input"}, {"TextControlBackgroundFocused","Input"},
            {"TextControlBorderBrush","Line"}, {"TextControlBorderBrushPointerOver","Dashed"}, {"TextControlBorderBrushFocused","Accent"},
            {"TextControlPlaceholderForeground","Faint"},
            {"ToolTipBackground","Panel"}, {"ToolTipForeground","Text"}, {"ToolTipBorderBrush","Line"},
            {"SliderTrackValueFill","Accent"}, {"SliderTrackValueFillPointerOver","Accent"}, {"SliderTrackValueFillPressed","Accent"},
            {"SliderThumbBackground","Accent"}, {"SliderThumbBackgroundPointerOver","Accent"}, {"SliderThumbBackgroundPressed","Accent"},
            {"SliderTrackFill","Line"}, {"SliderTrackFillPointerOver","Line"}, {"SliderTrackFillPressed","Line"},
        };

        public static void Init(Application app)
        {
            app.Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());
            app.Styles.Add(new DandyStyles());
            foreach (var kv in Palette) app.Resources[kv.Key] = new SolidColorBrush(Color.Parse(kv.Value));
            foreach (var kv in FluentKeys) app.Resources[kv.Key] = app.Resources[kv.Value];
            // Fluent's own parts and the window title bar follow this.
            app.RequestedThemeVariant = ThemeVariant.Dark;
        }
    }
}
