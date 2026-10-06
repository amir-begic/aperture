using Funk.Web.Data.Entities;

namespace Funk.Web.Features.Invoices;

public static class InvoiceSnapshot
{
    /// <summary>
    /// Copies your business details and the client's address into the invoice. Done when issuing
    /// (so the invoice never changes afterwards) and for draft previews (so they show current data).
    /// </summary>
    public static void Apply(Invoice invoice, BusinessSettings settings, Client client)
    {
        invoice.Seller = new InvoiceAddress
        {
            Name = settings.OwnerName,
            Company = string.IsNullOrWhiteSpace(settings.BusinessName) ? null : settings.BusinessName,
            Street = settings.Street,
            PostalCode = settings.PostalCode,
            City = settings.City,
            CountryCode = settings.CountryCode,
        };
        invoice.SellerIban = settings.Iban;
        invoice.SellerEmail = settings.Email;
        invoice.SellerPhone = settings.Phone;
        invoice.SellerVatNumber = settings.IsVatRegistered ? settings.VatNumber : null;
        invoice.VatRatePercent = settings.IsVatRegistered ? settings.VatRatePercent : null;

        invoice.Client = new InvoiceAddress
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
