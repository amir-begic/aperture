using Aperture.Web.Data.Entities;

namespace Aperture.Web.Features.Invoices;

public static class InvoiceSnapshot
{
    /// <summary>
    /// Copies your business details and the client's address into the invoice. Done when issuing
    /// (so the invoice never changes afterwards) and for draft previews (so they show current data).
    /// </summary>
    public static void Apply(Invoice invoice, BusinessSettings settings, Client client)
    {
        Apply((ClientDocument)invoice, settings, client);
        invoice.SellerIban = settings.Iban;
    }

    /// <summary>The same for a quote, or anything else that goes to a client: sender, recipient, VAT.</summary>
    public static void Apply(ClientDocument document, BusinessSettings settings, Client client)
    {
        document.Seller = new InvoiceAddress
        {
            Name = settings.OwnerName,
            Company = string.IsNullOrWhiteSpace(settings.BusinessName) ? null : settings.BusinessName,
            Street = settings.Street,
            PostalCode = settings.PostalCode,
            City = settings.City,
            CountryCode = settings.CountryCode,
        };
        document.SellerEmail = settings.Email;
        document.SellerPhone = settings.Phone;
        document.SellerVatNumber = settings.IsVatRegistered ? settings.VatNumber : null;
        document.VatRatePercent = settings.IsVatRegistered ? settings.VatRatePercent : null;

        document.Client = new InvoiceAddress
        {
            Name = client.Name,
            Company = client.Company,
            Street = client.Street,
            PostalCode = client.PostalCode,
            City = client.City,
            CountryCode = client.CountryCode,
        };
    }
}
