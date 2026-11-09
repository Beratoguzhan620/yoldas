namespace Yoldas.Core.Places;

/// <summary>A place to start a search or put a stop at: a provincial centre, or a town people often travel from.</summary>
public sealed record Place(string Name, string Province, double Latitude, double Longitude)
{
    public string Label => Name == Province ? Name : $"{Name}, {Province}";
}

/// <summary>
/// The 81 provincial centres and some towns, built in so search works without an outside geocoder. Names match
/// however they are typed: "canakkale", "Çanakkale" and "ÇANAKKALE" are the same.
/// </summary>
public static class Places
{
    public static readonly IReadOnlyList<Place> All =
    [
        new("Adana", "Adana", 37.0000, 35.3213),
        new("Adıyaman", "Adıyaman", 37.7648, 38.2786),
        new("Afyonkarahisar", "Afyonkarahisar", 38.7507, 30.5567),
        new("Ağrı", "Ağrı", 39.7191, 43.0503),
        new("Amasya", "Amasya", 40.6499, 35.8353),
        new("Ankara", "Ankara", 39.9334, 32.8597),
        new("Antalya", "Antalya", 36.8969, 30.7133),
        new("Artvin", "Artvin", 41.1828, 41.8183),
        new("Aydın", "Aydın", 37.8560, 27.8416),
        new("Balıkesir", "Balıkesir", 39.6484, 27.8826),
        new("Bilecik", "Bilecik", 40.1451, 29.9799),
        new("Bingöl", "Bingöl", 38.8847, 40.4939),
        new("Bitlis", "Bitlis", 38.4006, 42.1095),
        new("Bolu", "Bolu", 40.7350, 31.6061),
        new("Burdur", "Burdur", 37.7203, 30.2908),
        new("Bursa", "Bursa", 40.1885, 29.0610),
        new("Çanakkale", "Çanakkale", 40.1553, 26.4142),
        new("Çankırı", "Çankırı", 40.6013, 33.6134),
        new("Çorum", "Çorum", 40.5506, 34.9556),
        new("Denizli", "Denizli", 37.7765, 29.0864),
        new("Diyarbakır", "Diyarbakır", 37.9144, 40.2306),
        new("Edirne", "Edirne", 41.6818, 26.5623),
        new("Elazığ", "Elazığ", 38.6810, 39.2264),
        new("Erzincan", "Erzincan", 39.7500, 39.5000),
        new("Erzurum", "Erzurum", 39.9043, 41.2679),
        new("Eskişehir", "Eskişehir", 39.7767, 30.5206),
        new("Gaziantep", "Gaziantep", 37.0662, 37.3833),
        new("Giresun", "Giresun", 40.9128, 38.3895),
        new("Gümüşhane", "Gümüşhane", 40.4603, 39.4814),
        new("Hakkari", "Hakkari", 37.5833, 43.7333),
        new("Hatay", "Hatay", 36.2021, 36.1600),
        new("Isparta", "Isparta", 37.7648, 30.5566),
        new("Mersin", "Mersin", 36.8000, 34.6333),
        new("İstanbul", "İstanbul", 41.0082, 28.9784),
        new("İzmir", "İzmir", 38.4237, 27.1428),
        new("Kars", "Kars", 40.6013, 43.0975),
        new("Kastamonu", "Kastamonu", 41.3887, 33.7827),
        new("Kayseri", "Kayseri", 38.7312, 35.4787),
        new("Kırklareli", "Kırklareli", 41.7333, 27.2167),
        new("Kırşehir", "Kırşehir", 39.1425, 34.1709),
        new("Kocaeli", "Kocaeli", 40.8533, 29.8815),
        new("Konya", "Konya", 37.8746, 32.4932),
        new("Kütahya", "Kütahya", 39.4167, 29.9833),
        new("Malatya", "Malatya", 38.3552, 38.3095),
        new("Manisa", "Manisa", 38.6191, 27.4289),
        new("Kahramanmaraş", "Kahramanmaraş", 37.5858, 36.9371),
        new("Mardin", "Mardin", 37.3212, 40.7245),
        new("Muğla", "Muğla", 37.2153, 28.3636),
        new("Muş", "Muş", 38.9462, 41.7539),
        new("Nevşehir", "Nevşehir", 38.6939, 34.6857),
        new("Niğde", "Niğde", 37.9667, 34.6833),
        new("Ordu", "Ordu", 40.9839, 37.8764),
        new("Rize", "Rize", 41.0201, 40.5234),
        new("Sakarya", "Sakarya", 40.7569, 30.3783),
        new("Samsun", "Samsun", 41.2867, 36.3300),
        new("Siirt", "Siirt", 37.9333, 41.9500),
        new("Sinop", "Sinop", 42.0231, 35.1531),
        new("Sivas", "Sivas", 39.7477, 37.0179),
        new("Tekirdağ", "Tekirdağ", 40.9833, 27.5167),
        new("Tokat", "Tokat", 40.3167, 36.5500),
        new("Trabzon", "Trabzon", 41.0015, 39.7178),
        new("Tunceli", "Tunceli", 39.1079, 39.5401),
        new("Şanlıurfa", "Şanlıurfa", 37.1591, 38.7969),
        new("Uşak", "Uşak", 38.6823, 29.4082),
        new("Van", "Van", 38.4891, 43.4089),
        new("Yozgat", "Yozgat", 39.8181, 34.8147),
        new("Zonguldak", "Zonguldak", 41.4564, 31.7987),
        new("Aksaray", "Aksaray", 38.3687, 34.0370),
        new("Bayburt", "Bayburt", 40.2552, 40.2249),
        new("Karaman", "Karaman", 37.1759, 33.2287),
        new("Kırıkkale", "Kırıkkale", 39.8468, 33.5153),
        new("Batman", "Batman", 37.8812, 41.1351),
        new("Şırnak", "Şırnak", 37.5164, 42.4611),
        new("Bartın", "Bartın", 41.6344, 32.3375),
        new("Ardahan", "Ardahan", 41.1105, 42.7022),
        new("Iğdır", "Iğdır", 39.9237, 44.0450),
        new("Yalova", "Yalova", 40.6500, 29.2667),
        new("Karabük", "Karabük", 41.2061, 32.6204),
        new("Kilis", "Kilis", 36.7184, 37.1212),
        new("Osmaniye", "Osmaniye", 37.0742, 36.2478),
        new("Düzce", "Düzce", 40.8438, 31.1565),
        new("Atakum", "Samsun", 41.3330, 36.2710),
        new("Bafra", "Samsun", 41.5678, 35.9069),
        new("Çarşamba", "Samsun", 41.1992, 36.7275),
        new("Terme", "Samsun", 41.2092, 36.9739),
        new("Vezirköprü", "Samsun", 41.1436, 35.4544),
        new("Havza", "Samsun", 40.9700, 35.6622),
        new("Kavak", "Samsun", 41.0772, 36.0425),
        new("Ladik", "Samsun", 40.9119, 35.8961),
        new("Ünye", "Ordu", 41.1272, 37.2886),
        new("Fatsa", "Ordu", 41.0275, 37.5000),
        new("Merzifon", "Amasya", 40.8733, 35.4633),
        new("Boyabat", "Sinop", 41.4672, 34.7708),
        new("Tosya", "Kastamonu", 41.0150, 34.0386),
        new("Osmancık", "Çorum", 40.9775, 34.8044),
        new("Sungurlu", "Çorum", 40.1667, 34.3733),
        new("Gebze", "Kocaeli", 40.8028, 29.4307),
        new("Kadıköy", "İstanbul", 40.9900, 29.0300),
        new("Esenler Otogarı", "İstanbul", 41.0400, 28.8900),
        new("AŞTİ", "Ankara", 39.9180, 32.8100),
    ];

    public static IReadOnlyList<Place> Find(string? query, int limit = 8)
    {
        var q = Fold(query);
        if (q.Length == 0)
        {
            return [];
        }

        return All
            .Select(p => (Place: p, Name: Fold(p.Name), Province: Fold(p.Province)))
            .Where(p => p.Name.StartsWith(q, StringComparison.Ordinal) || p.Name.Contains(' ' + q, StringComparison.Ordinal) || p.Province.StartsWith(q, StringComparison.Ordinal))
            .OrderBy(p => p.Name.StartsWith(q, StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(p => p.Name == p.Province ? 0 : 1)
            .ThenBy(p => p.Name, StringComparer.Ordinal)
            .Take(Math.Clamp(limit, 1, 20))
            .Select(p => p.Place)
            .ToList();
    }

    /// <summary>Lower case without Turkish marks: ç→c, ğ→g, ı and İ→i, ö→o, ş→s, ü→u.</summary>
    public static string Fold(string? text)
    {
        var lower = (text ?? string.Empty).Trim().ToLower(System.Globalization.CultureInfo.GetCultureInfo("tr-TR"));
        var chars = lower.Select(c => c switch
        {
            'ç' => 'c', 'ğ' => 'g', 'ı' => 'i', 'ö' => 'o', 'ş' => 's', 'ü' => 'u', 'â' => 'a', 'î' => 'i', 'û' => 'u',
            _ => c,
        });
        return string.Join(' ', new string(chars.ToArray()).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
