namespace Aperture.Web.Data.Entities;

public enum QuoteStatus
{
    /// <summary>Editable, no number yet.</summary>
    Draft,

    /// <summary>Numbered and frozen; the PDF is archived. Waiting for the client's answer.</summary>
    Sent,

    /// <summary>The client said yes: the shoot is booked at the quoted price.</summary>
    Accepted,

    /// <summary>The client said no.</summary>
    Declined,
}
