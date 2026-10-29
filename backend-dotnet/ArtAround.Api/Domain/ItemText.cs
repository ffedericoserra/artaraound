namespace ArtAround.Api.Domain;

// PK (ItemId, Tone): one text per tone.
public class ItemText
{
    public long ItemId { get; set; }
    public Tone Tone { get; set; }
    public string Text { get; set; } = "";
}
