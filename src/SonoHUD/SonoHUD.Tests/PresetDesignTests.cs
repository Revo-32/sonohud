using SonoHUD.Core;

internal static class PresetDesignTests
{
    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    public static void Run(Action<string, Action> check)
    {
        check("Black Deck tokens and persisted user colors/fonts survive the new bundled default", () =>
        {
            foreach (string color in new[] { DesignTokens.Background, DesignTokens.Surface, DesignTokens.Elevated, DesignTokens.TextPrimary, DesignTokens.TextSecondary, DesignTokens.TextTertiary, DesignTokens.Accent, DesignTokens.Border, DesignTokens.Hover, DesignTokens.Pressed, DesignTokens.Selected, DesignTokens.Disabled, DesignTokens.Error, DesignTokens.Warning, DesignTokens.Success }) Require(SceneValidation.Color(color), "invalid token");
            foreach (string text in new[] { DesignTokens.TextPrimary, DesignTokens.TextSecondary, DesignTokens.TextTertiary, DesignTokens.Accent }) Require(Contrast(text, DesignTokens.Surface) >= 4.5, "opaque dark text contrast");
            Require(Contrast(DesignTokens.LightTextPrimary, DesignTokens.LightSurface) >= 4.5 && Contrast(DesignTokens.LightTextSecondary, DesignTokens.LightSurface) >= 4.5, "light text contrast");
            Require(new OverlayConfig().AccentColor == "#FFE4483D" && new OverlayConfig().FontFamily == "Wanted Sans", "new defaults");
            foreach (string font in new[] { "Segoe UI", "Consolas", "Wanted Sans", "Georgia" })
            foreach (string accent in new[] { "#FF87DFC4", "#FF999999", "#FF8FC6FF" })
            {
                var saved = new OverlayDocument { Overlay = new() { FontFamily = font, AccentColor = accent, Scale = 1.25, X = -230, Y = 41 } };
                string before = OverlayJson.Serialize(saved); var loaded = OverlayJson.Parse(before);
                foreach (PresetKind preset in Enum.GetValues<PresetKind>())
                {
                    loaded.Overlay.Preset = preset; var scene = OverlayPresets.Create(loaded.Overlay);
                    Require(scene.Elements.Where(e => e.Type != ElementKind.Group).All(e => e.Style.FontFamily == font), "saved font selection changed");
                    Require(scene.Elements.Single(e => e.Type == ElementKind.ProgressBar).Style.Foreground == accent, "saved accent changed");
                }
                Require(OverlayJson.Serialize(saved) == before && loaded.Overlay.FontFamily == font && loaded.Overlay.AccentColor == accent && loaded.Overlay.Scale == 1.25 && loaded.Overlay.X == -230, "saved document mutated");
            }
        });
        check("ten preset identities retain legacy enum values and allow breathing room below progress bars", () =>
        {
            Require((int)PresetKind.Compact == 0 && (int)PresetKind.MusicCard == 1 && (int)PresetKind.LyricsFocus == 2 && (int)PresetKind.Cinematic == 3, "legacy enum values changed");
            Require(OverlayPresets.Dimensions(PresetKind.Compact) == (380, 104) && OverlayPresets.Dimensions(PresetKind.MusicCard) == (460, 196) && OverlayPresets.Dimensions(PresetKind.LyricsFocus) == (480, 284) && OverlayPresets.Dimensions(PresetKind.Cinematic) == (560, 208), "preset dimensions changed unexpectedly");
            Require(OverlayPresets.Dimensions(PresetKind.AlbumTile) == (280, 424) && OverlayPresets.Dimensions(PresetKind.InfoRail) == (300, 300) && OverlayPresets.Dimensions(PresetKind.Poster) == (320, 440) && OverlayPresets.Dimensions(PresetKind.StatusStrip) == (640, 72), "remaining preset dimensions changed unexpectedly");
            var scenes = Enum.GetValues<PresetKind>().Select(p => OverlayPresets.Create(new() { Preset = p })).ToArray();
            Require(scenes.Length == 10 && scenes.Select(s => (s.Width, s.Height)).Distinct().Count() == 10, "preset size identities duplicated");
            SceneElement Item(PresetKind preset, ElementKind kind) => scenes.Single(s => s.Name == preset.ToString()).Elements.Single(e => e.Type == kind);
            var cardArt = Item(PresetKind.MusicCard, ElementKind.AlbumArtwork);
            Require(cardArt.Width == cardArt.Height && Item(PresetKind.MusicCard, ElementKind.TrackTitle).X > cardArt.X + cardArt.Width, "Music Card is not a split art/information layout");
            Require(Item(PresetKind.Cinematic, ElementKind.AlbumArtwork).X > 350 && Item(PresetKind.Cinematic, ElementKind.TrackTitle).Style.FontSize > Item(PresetKind.MusicCard, ElementKind.TrackTitle).Style.FontSize, "Cinematic lacks its large left title/right art identity");
            Require(Item(PresetKind.AlbumTile, ElementKind.AlbumArtwork).Y < Item(PresetKind.AlbumTile, ElementKind.TrackTitle).Y && Item(PresetKind.AlbumTile, ElementKind.AlbumArtwork).Width == 224, "Album Tile is not artwork-first");
            Require(Item(PresetKind.InfoRail, ElementKind.AlbumArtwork).Width == 88 && Item(PresetKind.InfoRail, ElementKind.PlaybackTime).Y < Item(PresetKind.InfoRail, ElementKind.TrackTitle).Y, "Info Rail lacks its compact art/time header");
            Require(Item(PresetKind.Poster, ElementKind.TrackTitle).Y < Item(PresetKind.Poster, ElementKind.AlbumArtwork).Y && Item(PresetKind.Poster, ElementKind.AlbumArtwork).Width > Item(PresetKind.Poster, ElementKind.AlbumArtwork).Height, "Poster is not a title-first editorial layout");
            Require(Item(PresetKind.LyricsFocus, ElementKind.LyricsText).Style.FontSize > Item(PresetKind.LyricsFocus, ElementKind.TrackTitle).Style.FontSize, "Lyrics hierarchy missing");
            var minimumProgressMargins = new Dictionary<PresetKind, double>
            {
                [PresetKind.Compact] = 10, [PresetKind.MusicCard] = 17, [PresetKind.LyricsFocus] = 11, [PresetKind.Cinematic] = 21,
                [PresetKind.AlbumTile] = 19, [PresetKind.InfoRail] = 18, [PresetKind.Poster] = 21, [PresetKind.StatusStrip] = 8, [PresetKind.LyricStrip] = 9, [PresetKind.LyricSleeve] = 13
            };
            foreach (var scene in scenes)
            {
                var preset = Enum.Parse<PresetKind>(scene.Name); var progress = scene.Elements.Single(e => e.Type == ElementKind.ProgressBar);
                Require(scene.Height - progress.Y - progress.Height >= minimumProgressMargins[preset], "album removal reduced the verified progress bottom margin: " + preset);
            }
        });
        check("all ten overlays omit album source status and buttons while only titles opt into marquee", () =>
        {
            foreach (PresetKind preset in Enum.GetValues<PresetKind>())
            {
                var config = new OverlayConfig { Preset = preset, Scale = .75, ShowControls = true };
                var first = OverlayPresets.Create(config); config.ShowControls = false; var second = OverlayPresets.Create(config);
                Require(SceneJson.Serialize(new() { Scenes = [first] }) == SceneJson.Serialize(new() { Scenes = [second] }), "legacy ShowControls changed read-only projection");
                Require(first.Elements.All(e => e.Type is not (ElementKind.PlaybackControls or ElementKind.PlaybackSource or ElementKind.PlaybackStatus or ElementKind.AlbumTitle)), "overlay still contains album/controls/source/status");
                Require(first.Elements.Single(e => e.Type == ElementKind.TrackTitle).Marquee && first.Elements.Where(e => e.Type != ElementKind.TrackTitle).All(e => !e.Marquee), "marquee scope is not title-only");
                Require(first.Elements.Count(e => e.Type == ElementKind.PlaybackTime) == 1, "live playback time role absent/duplicated");
                var level = first.Elements.Single(e => e.Type == ElementKind.Spectrum);
                Require(level.Id == OverlayPresets.ElementId(config.Id, "audio-level") && level.Width is >= 40 and <= 440 && level.Height is >= 12 and <= 32, "frequency region is missing or oversized");
                Require(level.Style.Text is "Bars" or "Curve" or "Mirror" or "Minimal", "unknown spectrum presentation");
                Require(first.Elements.All(e => e.Id != OverlayPresets.ElementId(config.Id, "album")), "hidden legacy album role still occupies layout space");
                foreach (var frame in SceneMath.Flatten(first).Where(f => f.Element.Type != ElementKind.Group))
                    Require(frame.Bounds.X >= 0 && frame.Bounds.Y >= 0 && frame.Bounds.X + frame.Bounds.Width <= first.Width + .01 && frame.Bounds.Y + frame.Bounds.Height <= first.Height + .01, "out-of-bounds content");
                var document = new OverlayDocument { Overlay = config with { ShowControls = true } };
                Require(OverlayJson.Parse(OverlayJson.Serialize(document)).Overlay.ShowControls, "legacy field was lost on round-trip");
            }
        });
        check("transparent backgrounds preserve foreground opacity and lyrics source attribution", () =>
        {
            foreach (PresetKind preset in Enum.GetValues<PresetKind>())
            foreach (OverlayTheme theme in Enum.GetValues<OverlayTheme>())
            {
                var scene = OverlayPresets.Create(new() { Preset = preset, Theme = theme, BackgroundOpacity = 0 });
                Require(scene.Elements.Single(e => e.Type == ElementKind.BackgroundPanel).Style.Background.StartsWith("#00", StringComparison.Ordinal), "background did not become transparent");
                Require(SceneMath.Flatten(scene).Where(f => f.Element.Type != ElementKind.Group).All(f => f.Opacity == 1), "background faded foreground content");
                Require(scene.Elements.Single(e => e.Type == ElementKind.AlbumArtwork).Style.Background.Length == 7, "art placeholder became translucent");
                if (OverlayPresets.ShowsLyrics(preset)) Require(scene.Elements.Single(e => e.Id == OverlayPresets.ElementId(scene.Id, "sample-label")).Style.Text.Contains("가사"), "online source attribution missing");
            }
        });
        check("every preset uses one background surface across all options and typography choices", () =>
        {
            foreach (PresetKind preset in Enum.GetValues<PresetKind>())
            foreach (OverlayTheme theme in Enum.GetValues<OverlayTheme>())
            for (int options = 0; options < 16; options++)
            {
                var config = new OverlayConfig { Preset = preset, Theme = theme, BackgroundOpacity = .5, ShowArtwork = (options & 1) != 0, ShowArtist = (options & 2) != 0, ShowProgress = (options & 4) != 0, ShowControls = (options & 8) != 0 };
                var scene = OverlayPresets.Create(config); var panel = scene.Elements.Single(e => e.Type == ElementKind.BackgroundPanel);
                Require(panel.Id == OverlayPresets.ElementId(config.Id, "background") && panel.X == 0 && panel.Y == 0 && panel.Width == scene.Width && panel.Height == scene.Height, "background split or missing region");
                Require(panel.Style.Background.StartsWith("#80", StringComparison.Ordinal), "background opacity is not literal 50%");
                Require(SceneJson.Serialize(new() { Scenes = [scene] }) == SceneJson.Serialize(new() { Scenes = [OverlayPresets.Create(config)] }), "projection IDs are not deterministic");
            }
        });
        check("text scale and title weight only alter typography inside fixed preset bounds", () =>
        {
            foreach (PresetKind preset in Enum.GetValues<PresetKind>())
            foreach (double textScale in new[] { .9, 1, 1.15 })
            foreach (int titleWeight in new[] { 0, 400, 500, 600, 700 })
            {
                var baseline = OverlayPresets.Create(new() { Preset = preset });
                var config = new OverlayConfig { Id = baseline.Id, Preset = preset, TextScale = textScale, TitleWeight = titleWeight };
                var scene = OverlayPresets.Create(config);
                var title = scene.Elements.Single(e => e.Type == ElementKind.TrackTitle);
                Require(title.Marquee && Math.Floor(title.Height / (title.Style.FontSize * 1.25)) == 1, "title no longer fits exactly one line at text scale " + textScale);
                foreach (var element in scene.Elements.Where(e => e.Type != ElementKind.Group))
                {
                    var original = baseline.Elements.Single(e => e.Id == element.Id);
                    Require((element.X, element.Y, element.Width, element.Height) == (original.X, original.Y, original.Width, original.Height), "typography changed layout bounds");
                    Require(Math.Abs(element.Style.FontSize - original.Style.FontSize * textScale) < .00001, "font scale not applied");
                    Require(element.Style.FontWeight == (element.Type == ElementKind.TrackTitle && titleWeight != 0 ? titleWeight : original.Style.FontWeight), "title weight leaked to metadata");
                }
            }
            var saved = new OverlayDocument { Overlay = new() { TextScale = 1.15, TitleWeight = 700 } };
            var restored = OverlayJson.Parse(OverlayJson.Serialize(saved));
            Require(restored.Overlay.TextScale == 1.15 && restored.Overlay.TitleWeight == 700, "typography settings failed round-trip");
        });
    }
    static double Contrast(string a, string b)
    {
        double L(string color)
        {
            string rgb = color[^6..]; double C(int offset) { double value = Convert.ToInt32(rgb.Substring(offset, 2), 16) / 255d; return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4); }
            return .2126 * C(0) + .7152 * C(2) + .0722 * C(4);
        }
        double x = L(a), y = L(b); return (Math.Max(x, y) + .05) / (Math.Min(x, y) + .05);
    }
}

