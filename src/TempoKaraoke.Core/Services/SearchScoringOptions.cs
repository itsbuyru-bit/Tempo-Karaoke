namespace TempoKaraoke.Services;

/// <summary>Centralized weights and limits for the search pipeline.</summary>
public sealed record SearchScoringOptions
{
    public static SearchScoringOptions Default { get; } = new();

    public int TitleExact { get; init; } = 5000;
    public int TitlePrefix { get; init; } = 4000;
    public int TitleContains { get; init; } = 3000;
    public int ArtistExact { get; init; } = 4000;
    public int ArtistPrefix { get; init; } = 3000;
    public int ArtistContains { get; init; } = 2000;
    public int FolderExact { get; init; } = 1200;
    public int FolderPrefix { get; init; } = 900;
    public int FolderContains { get; init; } = 600;

    public int TokenBase { get; init; } = 100;
    public int TitleTokenExact { get; init; } = 500;
    public int TitleTokenPrefix { get; init; } = 400;
    public int TitleTokenContains { get; init; } = 300;
    public int ArtistTokenExact { get; init; } = 400;
    public int ArtistTokenPrefix { get; init; } = 300;
    public int ArtistTokenContains { get; init; } = 200;
    public int FolderTokenExact { get; init; } = 120;
    public int FolderTokenPrefix { get; init; } = 90;
    public int FolderTokenContains { get; init; } = 60;

    public int RawPhraseExact { get; init; } = 450;
    public int RawPhrasePrefix { get; init; } = 350;
    public int RawPhraseContains { get; init; } = 250;
    public int RawTokenExact { get; init; } = 250;
    public int RawTokenPrefix { get; init; } = 200;
    public int RawTokenContains { get; init; } = 150;

    public int PhoneticTitleExact { get; init; } = 280;
    public int PhoneticTitlePrefix { get; init; } = 220;
    public int PhoneticTitleContains { get; init; } = 160;
    public int PhoneticArtistExact { get; init; } = 240;
    public int PhoneticArtistPrefix { get; init; } = 190;
    public int PhoneticArtistContains { get; init; } = 140;
    public int JoinedTitleExact { get; init; } = 180;
    public int JoinedTitlePrefix { get; init; } = 140;
    public int JoinedTitleContains { get; init; } = 100;
    public int JoinedArtistExact { get; init; } = 160;
    public int JoinedArtistPrefix { get; init; } = 120;
    public int JoinedArtistContains { get; init; } = 90;

    public double CyrillicPhoneticScale { get; init; } = 0.8;
    public double StemScale { get; init; } = 0.9;
    public int AbbreviationArtist { get; init; } = 320;
    public int AbbreviationTitle { get; init; } = 260;
    public int AbbreviationFolder { get; init; } = 80;
    public int FuzzyTitle { get; init; } = 60;
    public int FuzzyArtist { get; init; } = 50;
    public int FuzzyFolder { get; init; } = 20;
    public double FuzzyVariantMultiplier { get; init; } = 0.85;
    public double PhoneticFuzzyScale { get; init; } = 0.9;
    public int FuzzyMinLength { get; init; } = 4;
    public int FuzzyLongLength { get; init; } = 7;
    public int FuzzyShortErrors { get; init; } = 1;
    public int FuzzyLongErrors { get; init; } = 2;
    public int AbbreviationMinLength { get; init; } = 2;

    public double PartialFallbackMultiplier { get; init; } = 0.6;
    public int FallbackMaxMissing { get; init; } = 1;
    public int StrictResultThreshold { get; init; } = 3;
    public int FreeTextMinTokens { get; init; } = 5;
    public double FreeTextMinRatio { get; init; } = 0.6;
    public double FreeTextMultiplier { get; init; } = 0.75;

    public double PositionalMax { get; init; } = 0.08;
    public int SparsityThreshold { get; init; } = 4;
    public double SparsityPerToken { get; init; } = 0.02;
    public double SparsityMax { get; init; } = 0.12;
    public double ExactFloor { get; init; } = 0.94;
    public int MaxFieldLength { get; init; } = 200;
    public int StemMinLength { get; init; } = 4;

    public int FavoriteBonus { get; init; } = 150;
    public int PlayCountMax { get; init; } = 100;
    public int RecencyMax { get; init; } = 50;
    public int RecencyDays { get; init; } = 14;
    public int DiversityCap { get; init; } = 3;
}
