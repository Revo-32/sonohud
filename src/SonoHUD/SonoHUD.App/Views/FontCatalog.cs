using System.IO.Packaging;
using System.Windows.Media;

namespace SonoHUD.App.Views;

/// <summary>Bundled, licensed typefaces. Font choices never depend on OS font installation.</summary>
public static class FontCatalog
{
    public const string DefaultName = "Wanted Sans";
    public const string MonoName = "JetBrains Mono";

    public sealed record Entry(string Name, string ResourceFolder, string LanguageSupport, bool IsMonospaced = false)
    {
        public string ResourceFamily => $"./Assets/Fonts/{ResourceFolder}/#{Name}";
    }

    public static IReadOnlyList<Entry> Entries { get; } = Array.AsReadOnly<Entry>([
        new("Wanted Sans", "WantedSans", "한글·영문 / 일본어는 함께 제공되는 Pretendard JP"),
        new("Pretendard", "Pretendard", "한글·영문 / 일본어는 함께 제공되는 Pretendard JP"),
        new("Pretendard JP", "PretendardJP", "한글·영문·일본어 / 일본어 한자 형태 지원"),
        new("Inter", "Inter", "영문 / 한글·일본어는 함께 제공되는 보완 글꼴"),
        new("JetBrains Mono", "JetBrainsMono", "영문 고정폭 / 한글·일본어는 함께 제공되는 보완 글꼴", true)
    ]);

    public static string[] Choices { get; } = Entries.Select(entry => entry.Name).ToArray();

    // Names/normalization must remain usable before WPF has registered its pack URI parser.
    static readonly Lazy<IReadOnlyDictionary<string, FontFamily>> Families = new(CreateFamilies);

    static IReadOnlyDictionary<string, FontFamily> CreateFamilies()
    {
        // The public Packaging API registers the pack scheme before constructing a URI. Do not
        // rely on Application/Window initialization or register our own competing URI parser.
        // Explicit assembly identity also works when the renderer is hosted by the STA tests.
        Uri resourceRoot = new(PackUriHelper.Create(new Uri("application:///", UriKind.Absolute)), "SonoHUD;component/");
        return Entries.ToDictionary(
            entry => entry.Name,
            entry => new FontFamily(resourceRoot, string.Join(", ",
                new[] { entry.Name, DefaultName, "Pretendard JP" }
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(name => Entries.Single(candidate => candidate.Name == name).ResourceFamily))),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Resolves a UI selection without rewriting saved settings. Old OS-font selections and unknown
    /// names display with the new bundled default until the user explicitly chooses another font.
    /// </summary>
    public static string NormalizeSelection(string? selection)
    {
        if (!string.IsNullOrWhiteSpace(selection))
            foreach (string name in selection.Split(','))
            {
                Entry? match = Entries.FirstOrDefault(entry => string.Equals(entry.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
                if (match is not null) return match.Name;
            }
        return DefaultName;
    }

    public static FontFamily Resolve(string? selection) => Families.Value[NormalizeSelection(selection)];

    public static string Describe(string? selection) => Entries.Single(entry => entry.Name == NormalizeSelection(selection)).LanguageSupport;
}
