using System.Text;
using Codecrete.SwissQRBill.Generator;
using Funk.Web.Data.Entities;

namespace Funk.Web.Features.Invoices;

/// <summary>The Swiss QR-bill (payment part and receipt) at the bottom of an invoice.</summary>
public static class InvoiceQrBill
{
    public static Bill Create(Invoice invoice)
    {
        var amountDue = invoice.Totals.AmountDue;
        var bill = new Bill
        {
            Account = invoice.SellerIban,
            Creditor = ToAddress(invoice.Seller.Name, invoice.Seller),
            Currency = "CHF",
            // A QR-bill amount must be at least 0.01; without one, the payer fills it in.
            Amount = amountDue.Rappen > 0 ? amountDue.Chf : null,
            Debtor = invoice.Client.HasPostalAddress ? ToAddress(invoice.Client.Company ?? invoice.Client.Name, invoice.Client) : null,
            UnstructuredMessage = invoice.Number is null ? null : $"Invoice {invoice.Number}",
            Format = new BillFormat
            {
                Language = Language.EN,
                GraphicsFormat = GraphicsFormat.SVG,
                OutputSize = OutputSize.QrBillOnly,
                SeparatorType = SeparatorType.DashedLineWithScissors,
                FontFamily = InvoiceFonts.Family,
            },
        };

        // An RF creditor reference (ISO 11649) lets the bank report which invoice was paid.
        // It works with a normal IBAN; a QR-IBAN would need a different reference type.
        if (invoice.Number is not null)
        {
            bill.CreateAndSetCreditorReference(invoice.Number.Replace("-", ""));
        }

        return bill;
    }

    /// <summary>Problems that would make the QR-bill invalid, in plain words; empty when it's fine.</summary>
    public static IReadOnlyList<string> Problems(Invoice invoice)
    {
        if (string.IsNullOrWhiteSpace(invoice.SellerIban))
        {
            return ["Your IBAN is missing (Settings)."];
        }

        if (Payments.IsQrIban(invoice.SellerIban))
        {
            return ["Your IBAN is a QR-IBAN. Please enter the normal IBAN of the account in Settings."];
        }

        var result = QRBill.Validate(Create(invoice));
        return result.HasErrors
            ? result.ValidationMessages
                .Where(m => m.Type == ValidationMessage.MessageType.Error)
                .Select(m => $"QR-bill: {m.Field} ({m.MessageKey})")
                .ToList()
            : [];
    }

    public static string GenerateSvg(Invoice invoice) => Encoding.UTF8.GetString(QRBill.Generate(Create(invoice)));

    private static Address ToAddress(string name, InvoiceAddress address) => new()
    {
        Name = name,
        Street = address.Street,
        PostalCode = address.PostalCode,
        Town = address.City,
        CountryCode = address.CountryCode,
    };
}
