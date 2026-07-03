namespace RetroAudit.Models;

// Sol paneldeki platform listesinin (ListBox) her satırını temsil eder — ya bir kategori başlığı
// (ör. "CONSOLES", "OTHERS") ya da tek bir platform satırıdır. Tek bir liste içinde iki farklı
// satır türünü karıştırmak, WPF'te native "collapsible group header" desteği olmadığı için
// MainViewModel.RebuildPlatformListItems tarafından elle kuruluyor: "OTHERS" kategorisi
// varsayılan olarak kapalı geldiğinden, o kategorinin platform satırları sadece
// IsOthersExpanded=true olduğunda listeye ekleniyor.
public class PlatformListItem
{
    public bool IsHeader { get; init; }
    public bool IsPlatformRow => !IsHeader;

    // Sadece başlık satırları için: kategori adı ve (varsa) daralt/genişlet oku.
    public string HeaderText { get; init; } = string.Empty;
    public bool IsCollapsibleHeader { get; init; }
    public string ExpandGlyph { get; init; } = string.Empty;

    // Sadece platform satırları için.
    public Platform? Platform { get; init; }

    // "OTHERS" dışındaki başlık satırları tıklanamaz — sadece görsel bir ayraç. ItemContainerStyle
    // bu satırları IsHitTestVisible=False yaparak seçilemez hale getirir.
    public bool IsNonInteractiveHeader => IsHeader && !IsCollapsibleHeader;
}
