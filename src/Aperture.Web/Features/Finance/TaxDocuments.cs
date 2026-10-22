using System.Text.RegularExpressions;
using Aperture.Web.Data.Entities;

namespace Aperture.Web.Features.Finance;

/// <summary>A receipt, payslip or invoice in the tax export, with its number and file name in the ZIP.</summary>
public sealed record TaxDocument(string Number, Attachment Attachment, string ZipPath);

/// <summary>
/// Numbers the year's documents (R001, R002, …) so the statement, the spreadsheet and the files in the ZIP
/// point at each other: every income or cost line shows the number of its receipt.
/// </summary>
public sealed partial class TaxDocuments
{
    private readonly Dictionary<int, TaxDocument> _byAttachmentId = [];
    private readonly List<TaxDocument> _all = [];

    public IReadOnlyList<TaxDocument> All => _all;

    public static TaxDocuments For(IncomeStatement statement, IReadOnlyList<Invoice> invoices)
    {
        var documents = new TaxDocuments();
        foreach (var income in statement.Income.Where(i => i.Proof is not null))
        {
            documents.AddReceipt(income.Proof!, income.Date, $"Payment - {income.ShootTitle}");
        }
        foreach (var expense in statement.Expenses.Where(e => e.Receipt is not null))
        {
            documents.AddReceipt(expense.Receipt!, expense.Date, $"Cost - {expense.Description}");
        }
        foreach (var asset in statement.Assets.Where(a => a.Asset.Receipt is not null && a.Asset.PurchaseDate.Year == statement.Year))
        {
            documents.AddReceipt(asset.Asset.Receipt!, asset.Asset.PurchaseDate, $"Equipment - {asset.Asset.Name}");
        }
        foreach (var invoice in invoices.Where(i => i.Pdf is not null).OrderBy(i => i.Number))
        {
            documents.Add(invoice.Number!, invoice.Pdf!, $"Invoices/{SafeFileName($"Invoice {invoice.Number}")}.pdf");
        }

        return documents;
    }

    /// <summary>"R007" for an attachment in the export, or null.</summary>
    public string? NumberOf(Attachment? attachment) =>
        attachment is not null && _byAttachmentId.TryGetValue(attachment.Id, out var document) ? document.Number : null;

    private void AddReceipt(Attachment attachment, DateOnly date, string title)
    {
        if (_byAttachmentId.ContainsKey(attachment.Id))
        {
            return;
        }

        var number = $"R{_all.Count(d => d.Number.StartsWith('R')) + 1:000}";
        var extension = Path.GetExtension(attachment.FileName).ToLowerInvariant();
        Add(number, attachment, $"Receipts/{number} {date:yyyy-MM-dd} {SafeFileName(title)}{extension}");
    }

    private void Add(string number, Attachment attachment, string zipPath)
    {
        var document = new TaxDocument(number, attachment, zipPath);
        _byAttachmentId[attachment.Id] = document;
        _all.Add(document);
    }

    /// <summary>Keeps letters (incl. umlauts), digits, spaces and a few separators; at most 80 characters.</summary>
    public static string SafeFileName(string text)
    {
        var safe = UnsafeCharacters().Replace(text, " ");
        safe = MultipleSpaces().Replace(safe, " ").Trim();
        return safe.Length > 80 ? safe[..80].TrimEnd() : safe;
    }

    [GeneratedRegex(@"[^\p{L}\p{Nd} .,&()+\-]")]
    private static partial Regex UnsafeCharacters();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex MultipleSpaces();
}
