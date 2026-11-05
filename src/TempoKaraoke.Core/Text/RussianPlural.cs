namespace TempoKaraoke.Text;

/// <summary>Русские формы множественного числа: 1 песня, 2 песни, 5 песен.</summary>
public static class RussianPlural
{
    public static string Word(int count, string one, string few, string many)
    {
        var absolute = Math.Abs(count);
        var mod100 = absolute % 100;
        var mod10 = absolute % 10;

        if (mod100 is >= 11 and <= 14)
        {
            return many;
        }

        return mod10 switch
        {
            1 => one,
            2 or 3 or 4 => few,
            _ => many,
        };
    }

    public static string Format(int count, string one, string few, string many) =>
        $"{count} {Word(count, one, few, many)}";
}
